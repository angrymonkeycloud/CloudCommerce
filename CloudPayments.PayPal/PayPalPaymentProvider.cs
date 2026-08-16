using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AngryMonkey.Cloud.Geography;

namespace AngryMonkey.CloudPayments.PayPal;

public sealed class PayPalPaymentProvider(HttpClient httpClient, PayPalOptions options) : IPaymentProvider
{
    private readonly SemaphoreSlim _tokenGate = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;

    public string Name => "PayPal";

    public PaymentProviderCapabilities Capabilities => PaymentProviderCapabilities.Create | PaymentProviderCapabilities.Authorize | PaymentProviderCapabilities.Capture | PaymentProviderCapabilities.Void | PaymentProviderCapabilities.Refund | PaymentProviderCapabilities.PartialRefund | PaymentProviderCapabilities.SavedPaymentMethods | PaymentProviderCapabilities.RecurringPayments | PaymentProviderCapabilities.Webhooks;

    public Task<PaymentResult> CreateAsync(PaymentRequest request, CancellationToken cancellationToken = default)
        => CreateOrderAsync(request, "CAPTURE", cancellationToken);

    public Task<PaymentResult> AuthorizeAsync(PaymentRequest request, CancellationToken cancellationToken = default)
        => CreateOrderAsync(request, "AUTHORIZE", cancellationToken);

    public Task<PaymentResult> CaptureAsync(CaptureRequest request, CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Post, $"v2/checkout/orders/{Uri.EscapeDataString(request.PaymentId)}/capture", new { }, request.IdempotencyKey, cancellationToken);

    public Task<PaymentResult> VoidAsync(VoidPaymentRequest request, CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Post, $"v2/payments/authorizations/{Uri.EscapeDataString(request.PaymentId)}/void", new { }, request.IdempotencyKey, cancellationToken, request.PaymentId, PaymentStatuses.Voided);

    public Task<PaymentResult> RefundAsync(RefundRequest request, CancellationToken cancellationToken = default)
    {
        object body = new { amount = Amount(request.Amount), note_to_payer = request.Reason };
        return SendAsync(HttpMethod.Post, $"v2/payments/captures/{Uri.EscapeDataString(request.PaymentId)}/refund", body, request.IdempotencyKey, cancellationToken, request.PaymentId, request.IsPartial ? PaymentStatuses.PartiallyRefunded : PaymentStatuses.Refunded, request.Amount);
    }

    public Task<PaymentResult> ChargeRecurringAsync(RecurringPaymentRequest request, CancellationToken cancellationToken = default)
    {
        PaymentRequest paymentRequest = new()
        {
            Provider = Name,
            Amount = request.Amount,
            IdempotencyKey = request.IdempotencyKey,
            CustomerReference = request.CustomerReference,
            PaymentMethod = request.PaymentMethod,
            Metadata = request.Metadata ?? []
        };
        return CreateOrderAsync(paymentRequest, "CAPTURE", cancellationToken);
    }

    public async Task<Payment?> GetAsync(string paymentId, CancellationToken cancellationToken = default)
    {
        PaymentResult order = await SendAsync(HttpMethod.Get, $"v2/checkout/orders/{Uri.EscapeDataString(paymentId)}", null, null, cancellationToken);
        if (order.IsSuccessful)
            return order.Payment;

        PaymentResult capture = await SendAsync(HttpMethod.Get, $"v2/payments/captures/{Uri.EscapeDataString(paymentId)}", null, null, cancellationToken);
        return capture.Payment;
    }

    public async Task<PaymentStatuses?> GetStatusAsync(string paymentId, CancellationToken cancellationToken = default)
        => (await GetAsync(paymentId, cancellationToken))?.Status;

    public async Task<bool> ValidateWebhookAsync(PaymentWebhookRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(options.WebhookId))
            return false;

        string[] requiredHeaders = ["PAYPAL-AUTH-ALGO", "PAYPAL-CERT-URL", "PAYPAL-TRANSMISSION-ID", "PAYPAL-TRANSMISSION-SIG", "PAYPAL-TRANSMISSION-TIME"];
        if (requiredHeaders.Any(name => !TryGetHeader(request.Headers, name, out _)))
            return false;

        using JsonDocument eventDocument = JsonDocument.Parse(request.Body);
        Dictionary<string, object?> body = new()
        {
            ["auth_algo"] = Header(request.Headers, "PAYPAL-AUTH-ALGO"),
            ["cert_url"] = Header(request.Headers, "PAYPAL-CERT-URL"),
            ["transmission_id"] = Header(request.Headers, "PAYPAL-TRANSMISSION-ID"),
            ["transmission_sig"] = Header(request.Headers, "PAYPAL-TRANSMISSION-SIG"),
            ["transmission_time"] = Header(request.Headers, "PAYPAL-TRANSMISSION-TIME"),
            ["webhook_id"] = options.WebhookId,
            ["webhook_event"] = eventDocument.RootElement.Clone()
        };

        string token = await GetAccessTokenAsync(cancellationToken);
        using HttpRequestMessage message = new(HttpMethod.Post, "v1/notifications/verify-webhook-signature") { Content = JsonContent.Create(body) };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using HttpResponseMessage response = await httpClient.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return false;

        using JsonDocument result = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return result.RootElement.TryGetProperty("verification_status", out JsonElement status) && status.GetString() == "SUCCESS";
    }

    public Task<PaymentProviderEvent> ParseWebhookAsync(PaymentWebhookRequest request, CancellationToken cancellationToken = default)
    {
        using JsonDocument document = JsonDocument.Parse(request.Body);
        JsonElement root = document.RootElement;
        JsonElement resource = root.TryGetProperty("resource", out JsonElement value) ? value : default;
        string? paymentId = resource.ValueKind == JsonValueKind.Object && resource.TryGetProperty("id", out JsonElement id) ? id.GetString() : null;
        DateTimeOffset occurredAt = root.TryGetProperty("create_time", out JsonElement created) && DateTimeOffset.TryParse(created.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed) ? parsed : DateTimeOffset.UtcNow;
        Dictionary<string, string> data = resource.ValueKind == JsonValueKind.Undefined ? [] : new() { ["resource"] = resource.GetRawText() };
        return Task.FromResult(new PaymentProviderEvent(Name, root.GetProperty("id").GetString()!, root.GetProperty("event_type").GetString()!, paymentId, occurredAt, data));
    }

    private async Task<PaymentResult> CreateOrderAsync(PaymentRequest request, string intent, CancellationToken cancellationToken)
    {
        Dictionary<string, object?> purchaseUnit = new()
        {
            ["reference_id"] = request.Metadata.GetValueOrDefault("orderId") ?? request.IdempotencyKey,
            ["description"] = request.Description,
            ["custom_id"] = request.CustomerReference,
            ["amount"] = Amount(request.Amount)
        };
        Dictionary<string, object?> body = new()
        {
            ["intent"] = intent,
            ["purchase_units"] = new[] { purchaseUnit }
        };

        if (request.PaymentMethod is not null)
            body["payment_source"] = new Dictionary<string, object?> { ["token"] = new { id = request.PaymentMethod.Reference, type = "BILLING_AGREEMENT" } };

        return await SendAsync(HttpMethod.Post, "v2/checkout/orders", body, request.IdempotencyKey, cancellationToken);
    }

    private async Task<PaymentResult> SendAsync(HttpMethod method, string path, object? body, string? idempotencyKey, CancellationToken cancellationToken, string? fallbackPaymentId = null, PaymentStatuses? fallbackStatus = null, Money? fallbackAmount = null)
    {
        options.Validate();
        string token = await GetAccessTokenAsync(cancellationToken);
        using HttpRequestMessage message = new(method, path);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
            message.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);
        if (body is not null && method != HttpMethod.Get)
            message.Content = JsonContent.Create(body);

        using HttpResponseMessage response = await httpClient.SendAsync(message, cancellationToken);
        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        using JsonDocument document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        if (!response.IsSuccessStatusCode)
            return PaymentResult.Failed(MapError(document.RootElement, (int)response.StatusCode));

        if (fallbackPaymentId is not null && fallbackStatus is not null)
        {
            string providerReference = document.RootElement.TryGetProperty("id", out JsonElement resultId) ? resultId.GetString()! : fallbackPaymentId;
            Money amount = fallbackAmount ?? ReadAmount(document.RootElement) ?? new("USD", 0m);
            return new(new Payment { Id = fallbackPaymentId, Provider = Name, ProviderReference = providerReference, Amount = amount, Status = fallbackStatus.Value });
        }

        return new(MapPayment(document.RootElement));
    }

    private Payment MapPayment(JsonElement element)
    {
        string id = element.GetProperty("id").GetString()!;
        string status = element.TryGetProperty("status", out JsonElement statusElement) ? statusElement.GetString()! : "PENDING";
        Money amount = ReadAmount(element) ?? new("USD", 0m);
        Uri? approvalUrl = null;
        if (element.TryGetProperty("links", out JsonElement links))
        {
            foreach (JsonElement link in links.EnumerateArray())
            {
                if (link.TryGetProperty("rel", out JsonElement relation) && relation.GetString() is "approve" or "payer-action" && link.TryGetProperty("href", out JsonElement href))
                {
                    Uri.TryCreate(href.GetString(), UriKind.Absolute, out approvalUrl);
                    break;
                }
            }
        }

        PaymentStatuses mappedStatus = status switch
        {
            "COMPLETED" => PaymentStatuses.Captured,
            "APPROVED" => PaymentStatuses.Authorized,
            "VOIDED" => PaymentStatuses.Voided,
            "REFUNDED" => PaymentStatuses.Refunded,
            "PARTIALLY_REFUNDED" => PaymentStatuses.PartiallyRefunded,
            "PAYER_ACTION_REQUIRED" or "CREATED" or "SAVED" when approvalUrl is not null => PaymentStatuses.RequiresAction,
            "DECLINED" or "FAILED" => PaymentStatuses.Failed,
            _ => PaymentStatuses.Pending
        };

        return new()
        {
            Id = id,
            Provider = Name,
            ProviderReference = id,
            Amount = amount,
            Status = mappedStatus,
            NextAction = approvalUrl is null ? null : new(PaymentActionTypes.Redirect, approvalUrl)
        };
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is not null && _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
            return _accessToken;

        await _tokenGate.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null && _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
                return _accessToken;

            options.Validate();
            using HttpRequestMessage message = new(HttpMethod.Post, "v1/oauth2/token");
            string credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.ClientId}:{options.ClientSecret}"));
            message.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            message.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials" });
            using HttpResponseMessage response = await httpClient.SendAsync(message, cancellationToken);
            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            response.EnsureSuccessStatusCode();
            using JsonDocument document = JsonDocument.Parse(json);
            _accessToken = document.RootElement.GetProperty("access_token").GetString()!;
            int expiresIn = document.RootElement.TryGetProperty("expires_in", out JsonElement expires) ? expires.GetInt32() : 300;
            _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
            return _accessToken;
        }
        finally
        {
            _tokenGate.Release();
        }
    }

    private static Dictionary<string, string> Amount(Money amount) => new() { ["currency_code"] = amount.Currency.ToUpperInvariant(), ["value"] = amount.Value.ToString("0.00", CultureInfo.InvariantCulture) };

    private static Money? ReadAmount(JsonElement element)
    {
        if (element.TryGetProperty("amount", out JsonElement amount))
            return ParseAmount(amount);
        if (element.TryGetProperty("purchase_units", out JsonElement purchaseUnits) && purchaseUnits.GetArrayLength() > 0)
        {
            JsonElement unit = purchaseUnits[0];
            if (unit.TryGetProperty("amount", out amount))
                return ParseAmount(amount);
            if (unit.TryGetProperty("payments", out JsonElement payments))
            {
                foreach (string collection in new[] { "captures", "authorizations", "refunds" })
                    if (payments.TryGetProperty(collection, out JsonElement entries) && entries.GetArrayLength() > 0 && entries[0].TryGetProperty("amount", out amount))
                        return ParseAmount(amount);
            }
        }
        return null;
    }

    private static Money ParseAmount(JsonElement amount)
    {
        string currency = amount.GetProperty("currency_code").GetString()!;
        decimal value = decimal.Parse(amount.GetProperty("value").GetString()!, CultureInfo.InvariantCulture);
        return new(currency, value);
    }

    private static PaymentError MapError(JsonElement root, int statusCode)
    {
        string code = root.TryGetProperty("name", out JsonElement name) ? name.GetString() ?? statusCode.ToString(CultureInfo.InvariantCulture) : statusCode.ToString(CultureInfo.InvariantCulture);
        string message = root.TryGetProperty("message", out JsonElement messageElement) ? messageElement.GetString() ?? "PayPal request failed." : "PayPal request failed.";
        PaymentErrorTypes type = statusCode switch { 401 or 403 => PaymentErrorTypes.Authentication, 409 => PaymentErrorTypes.Duplicate, 422 => PaymentErrorTypes.Declined, 429 => PaymentErrorTypes.RateLimited, >= 500 => PaymentErrorTypes.ProviderUnavailable, _ => PaymentErrorTypes.InvalidRequest };
        return new(type, code, message, code, statusCode is 409 or 429 or >= 500);
    }

    private static string Header(IReadOnlyDictionary<string, string> headers, string name) => TryGetHeader(headers, name, out string? value) ? value! : string.Empty;

    private static bool TryGetHeader(IReadOnlyDictionary<string, string> headers, string name, out string? value)
    {
        KeyValuePair<string, string> header = headers.FirstOrDefault(pair => pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase));
        value = header.Value;
        return value is not null;
    }
}
