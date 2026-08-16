using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AngryMonkey.Cloud.Geography;

namespace AngryMonkey.CloudPayments.Stripe;

public sealed class StripePaymentProvider(HttpClient httpClient, StripeOptions options) : IPaymentProvider
{
    private static readonly HashSet<string> ZeroDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase) { "BIF", "CLP", "DJF", "GNF", "JPY", "KMF", "KRW", "MGA", "PYG", "RWF", "UGX", "VND", "VUV", "XAF", "XOF", "XPF" };

    public string Name => "Stripe";

    public PaymentProviderCapabilities Capabilities => PaymentProviderCapabilities.Create | PaymentProviderCapabilities.Authorize | PaymentProviderCapabilities.Capture | PaymentProviderCapabilities.Void | PaymentProviderCapabilities.Refund | PaymentProviderCapabilities.PartialRefund | PaymentProviderCapabilities.SavedPaymentMethods | PaymentProviderCapabilities.RecurringPayments | PaymentProviderCapabilities.Webhooks;

    public Task<PaymentResult> CreateAsync(PaymentRequest request, CancellationToken cancellationToken = default)
        => CreateIntentAsync(request, false, false, cancellationToken);

    public Task<PaymentResult> AuthorizeAsync(PaymentRequest request, CancellationToken cancellationToken = default)
        => CreateIntentAsync(request, true, false, cancellationToken);

    public async Task<PaymentResult> CaptureAsync(CaptureRequest request, CancellationToken cancellationToken = default)
    {
        Dictionary<string, string> values = [];
        if (request.Amount is not null)
            values["amount_to_capture"] = ToMinorUnits(request.Amount).ToString(CultureInfo.InvariantCulture);

        return await SendFormAsync(HttpMethod.Post, $"v1/payment_intents/{Uri.EscapeDataString(request.PaymentId)}/capture", values, request.IdempotencyKey, cancellationToken);
    }

    public Task<PaymentResult> VoidAsync(VoidPaymentRequest request, CancellationToken cancellationToken = default)
        => SendFormAsync(HttpMethod.Post, $"v1/payment_intents/{Uri.EscapeDataString(request.PaymentId)}/cancel", new Dictionary<string, string>(), request.IdempotencyKey, cancellationToken);

    public Task<PaymentResult> RefundAsync(RefundRequest request, CancellationToken cancellationToken = default)
    {
        Dictionary<string, string> values = new()
        {
            ["payment_intent"] = request.PaymentId,
            ["amount"] = ToMinorUnits(request.Amount).ToString(CultureInfo.InvariantCulture)
        };
        if (!string.IsNullOrWhiteSpace(request.Reason) && request.Reason is "duplicate" or "fraudulent" or "requested_by_customer")
            values["reason"] = request.Reason;

        return SendFormAsync(HttpMethod.Post, "v1/refunds", values, request.IdempotencyKey, cancellationToken, request.PaymentId, request.Amount, request.IsPartial ? PaymentStatuses.PartiallyRefunded : PaymentStatuses.Refunded);
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
        return CreateIntentAsync(paymentRequest, false, true, cancellationToken);
    }

    public async Task<Payment?> GetAsync(string paymentId, CancellationToken cancellationToken = default)
    {
        PaymentResult result = await SendFormAsync(HttpMethod.Get, $"v1/payment_intents/{Uri.EscapeDataString(paymentId)}", new Dictionary<string, string>(), null, cancellationToken);
        return result.Payment;
    }

    public async Task<PaymentStatuses?> GetStatusAsync(string paymentId, CancellationToken cancellationToken = default)
        => (await GetAsync(paymentId, cancellationToken))?.Status;

    public Task<bool> ValidateWebhookAsync(PaymentWebhookRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(options.WebhookSecret) || !TryGetHeader(request.Headers, "Stripe-Signature", out string? signatureHeader))
            return Task.FromResult(false);

        Dictionary<string, string> values = signatureHeader!.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .GroupBy(parts => parts[0], StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last()[1], StringComparer.Ordinal);

        if (!values.TryGetValue("t", out string? timestampText) || !values.TryGetValue("v1", out string? signature) || !long.TryParse(timestampText, CultureInfo.InvariantCulture, out long timestamp))
            return Task.FromResult(false);

        DateTimeOffset occurredAt = DateTimeOffset.FromUnixTimeSeconds(timestamp);
        if (DateTimeOffset.UtcNow - occurredAt is TimeSpan age && (age.Duration() > options.WebhookTolerance))
            return Task.FromResult(false);

        string payload = $"{timestampText}.{Encoding.UTF8.GetString(request.Body.Span)}";
        byte[] expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(options.WebhookSecret), Encoding.UTF8.GetBytes(payload));
        bool valid = TryDecodeHex(signature, out byte[] actual) && CryptographicOperations.FixedTimeEquals(expected, actual);
        return Task.FromResult(valid);
    }

    public Task<PaymentProviderEvent> ParseWebhookAsync(PaymentWebhookRequest request, CancellationToken cancellationToken = default)
    {
        using JsonDocument document = JsonDocument.Parse(request.Body);
        JsonElement root = document.RootElement;
        JsonElement payment = root.GetProperty("data").GetProperty("object");
        string? paymentId = payment.TryGetProperty("id", out JsonElement id) ? id.GetString() : null;
        DateTimeOffset occurredAt = root.TryGetProperty("created", out JsonElement created) ? DateTimeOffset.FromUnixTimeSeconds(created.GetInt64()) : DateTimeOffset.UtcNow;
        Dictionary<string, string> data = new() { ["object"] = payment.GetRawText() };
        return Task.FromResult(new PaymentProviderEvent(Name, root.GetProperty("id").GetString()!, root.GetProperty("type").GetString()!, paymentId, occurredAt, data));
    }

    private async Task<PaymentResult> CreateIntentAsync(PaymentRequest request, bool manualCapture, bool offSession, CancellationToken cancellationToken)
    {
        Dictionary<string, string> values = new()
        {
            ["amount"] = ToMinorUnits(request.Amount).ToString(CultureInfo.InvariantCulture),
            ["currency"] = request.Amount.Currency.ToLowerInvariant(),
            ["capture_method"] = manualCapture ? "manual" : "automatic"
        };

        if (!string.IsNullOrWhiteSpace(request.Description))
            values["description"] = request.Description;
        if (!string.IsNullOrWhiteSpace(request.CustomerReference))
            values["customer"] = request.CustomerReference;
        if (request.PaymentMethod is not null)
        {
            values["payment_method"] = request.PaymentMethod.Reference;
            values["confirm"] = "true";
        }
        if (offSession)
            values["off_session"] = "true";
        foreach ((string key, string value) in request.Metadata)
            values[$"metadata[{key}]"] = value;

        return await SendFormAsync(HttpMethod.Post, "v1/payment_intents", values, request.IdempotencyKey, cancellationToken);
    }

    private async Task<PaymentResult> SendFormAsync(HttpMethod method, string path, IReadOnlyDictionary<string, string> values, string? idempotencyKey, CancellationToken cancellationToken, string? fallbackPaymentId = null, Money? fallbackAmount = null, PaymentStatuses? fallbackStatus = null)
    {
        options.Validate();
        using HttpRequestMessage message = new(method, path);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.SecretKey);
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
            message.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        if (method != HttpMethod.Get)
            message.Content = new FormUrlEncodedContent(values);

        using HttpResponseMessage response = await httpClient.SendAsync(message, cancellationToken);
        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        using JsonDocument document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        if (!response.IsSuccessStatusCode)
            return PaymentResult.Failed(MapError(document.RootElement, (int)response.StatusCode));

        if (fallbackPaymentId is not null && document.RootElement.TryGetProperty("object", out JsonElement objectType) && objectType.GetString() == "refund")
        {
            Payment payment = new() { Id = fallbackPaymentId, Provider = Name, ProviderReference = document.RootElement.GetProperty("id").GetString(), Amount = fallbackAmount!, Status = fallbackStatus ?? PaymentStatuses.Refunded };
            return new(payment);
        }

        return new(MapPayment(document.RootElement));
    }

    private Payment MapPayment(JsonElement element)
    {
        string currency = element.TryGetProperty("currency", out JsonElement currencyElement) ? currencyElement.GetString()!.ToUpperInvariant() : "USD";
        long amount = element.TryGetProperty("amount", out JsonElement amountElement) ? amountElement.GetInt64() : 0;
        string status = element.TryGetProperty("status", out JsonElement statusElement) ? statusElement.GetString()! : "processing";
        string? clientSecret = element.TryGetProperty("client_secret", out JsonElement secretElement) ? secretElement.GetString() : null;
        Uri? redirect = null;
        if (element.TryGetProperty("next_action", out JsonElement nextAction) && nextAction.ValueKind == JsonValueKind.Object && nextAction.TryGetProperty("redirect_to_url", out JsonElement redirectObject) && redirectObject.TryGetProperty("url", out JsonElement urlElement))
            Uri.TryCreate(urlElement.GetString(), UriKind.Absolute, out redirect);

        PaymentAction? action = redirect is not null ? new(PaymentActionTypes.Redirect, redirect, clientSecret) : status == "requires_action" && clientSecret is not null ? new(PaymentActionTypes.ClientSecret, ClientSecret: clientSecret) : null;
        string id = element.GetProperty("id").GetString()!;
        return new()
        {
            Id = id,
            Provider = Name,
            ProviderReference = id,
            Amount = new(currency, FromMinorUnits(currency, amount)),
            Status = status switch
            {
                "requires_action" or "requires_confirmation" or "requires_payment_method" => PaymentStatuses.RequiresAction,
                "requires_capture" => PaymentStatuses.Authorized,
                "succeeded" => PaymentStatuses.Captured,
                "canceled" => PaymentStatuses.Cancelled,
                "processing" => PaymentStatuses.Pending,
                _ => PaymentStatuses.Pending
            },
            NextAction = action,
            CreatedAt = element.TryGetProperty("created", out JsonElement created) ? DateTimeOffset.FromUnixTimeSeconds(created.GetInt64()) : DateTimeOffset.UtcNow
        };
    }

    private static PaymentError MapError(JsonElement root, int statusCode)
    {
        JsonElement error = root.TryGetProperty("error", out JsonElement value) ? value : root;
        string code = error.TryGetProperty("code", out JsonElement codeElement) ? codeElement.GetString() ?? statusCode.ToString(CultureInfo.InvariantCulture) : statusCode.ToString(CultureInfo.InvariantCulture);
        string message = error.TryGetProperty("message", out JsonElement messageElement) ? messageElement.GetString() ?? "Stripe request failed." : "Stripe request failed.";
        PaymentErrorTypes type = statusCode switch { 401 or 403 => PaymentErrorTypes.Authentication, 429 => PaymentErrorTypes.RateLimited, >= 500 => PaymentErrorTypes.ProviderUnavailable, _ => code.Contains("declin", StringComparison.OrdinalIgnoreCase) ? PaymentErrorTypes.Declined : PaymentErrorTypes.InvalidRequest };
        return new(type, code, message, code, statusCode is 409 or 429 or >= 500);
    }

    private static long ToMinorUnits(Money money)
    {
        decimal multiplier = ZeroDecimalCurrencies.Contains(money.Currency) ? 1m : 100m;
        return checked((long)decimal.Round(money.Value * multiplier, 0, MidpointRounding.AwayFromZero));
    }

    private static decimal FromMinorUnits(string currency, long amount) => ZeroDecimalCurrencies.Contains(currency) ? amount : amount / 100m;

    private static bool TryGetHeader(IReadOnlyDictionary<string, string> headers, string name, out string? value)
    {
        KeyValuePair<string, string> header = headers.FirstOrDefault(pair => pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase));
        value = header.Value;
        return value is not null;
    }

    private static bool TryDecodeHex(string value, out byte[] bytes)
    {
        try { bytes = Convert.FromHexString(value); return true; }
        catch (FormatException) { bytes = []; return false; }
    }
}
