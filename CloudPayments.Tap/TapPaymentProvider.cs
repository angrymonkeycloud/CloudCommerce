using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AngryMonkey.Cloud.Geography;

namespace AngryMonkey.CloudPayments.Tap;

public sealed class TapPaymentProvider(HttpClient httpClient, TapOptions options) : IPaymentProvider
{
    private static readonly HashSet<string> ThreeDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "BHD", "JOD", "KWD", "OMR"
    };

    public string Name => "Tap";

    public PaymentProviderCapabilities Capabilities =>
        PaymentProviderCapabilities.Create |
        PaymentProviderCapabilities.Authorize |
        PaymentProviderCapabilities.Capture |
        PaymentProviderCapabilities.Void |
        PaymentProviderCapabilities.Refund |
        PaymentProviderCapabilities.PartialRefund |
        PaymentProviderCapabilities.SavedPaymentMethods |
        PaymentProviderCapabilities.Webhooks;

    public Task<PaymentResult> CreateAsync(PaymentRequest request, CancellationToken cancellationToken = default)
        => SendPaymentAsync("v2/charges/", CreatePaymentPayload(request), request.Amount, cancellationToken);

    public Task<PaymentResult> AuthorizeAsync(PaymentRequest request, CancellationToken cancellationToken = default)
        => SendPaymentAsync("v2/authorize/", CreatePaymentPayload(request), request.Amount, cancellationToken);

    public async Task<PaymentResult> CaptureAsync(CaptureRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Amount is null)
            return Unsupported("capture_amount_required", "Tap capture requires an amount.");

        Dictionary<string, object?> payload = new()
        {
            ["amount"] = request.Amount.Value,
            ["currency"] = request.Amount.Currency,
            ["source"] = new { id = request.PaymentId },
            ["reference"] = new { transaction = request.IdempotencyKey ?? request.PaymentId },
            ["customer"] = new { first_name = "CloudPayments", email = "payments@example.invalid" }
        };
        AddMerchant(payload);
        return await SendPaymentAsync("v2/charges/", payload, request.Amount, cancellationToken);
    }

    public async Task<PaymentResult> VoidAsync(VoidPaymentRequest request, CancellationToken cancellationToken = default)
    {
        Dictionary<string, object?> payload = new();
        ApiResponse api = await SendAsync(HttpMethod.Post, $"v2/authorize/{Uri.EscapeDataString(request.PaymentId)}/void", payload, cancellationToken);
        if (!api.IsSuccess)
            return PaymentResult.Failed(api.Error!);

        return new(MapPayment(api.Root, new Money(GetString(api.Root, "currency") ?? "KWD", GetDecimal(api.Root, "amount") ?? 0m)));
    }

    public async Task<PaymentResult> RefundAsync(RefundRequest request, CancellationToken cancellationToken = default)
    {
        Dictionary<string, object?> payload = new()
        {
            ["charge_id"] = request.PaymentId,
            ["amount"] = request.Amount.Value,
            ["currency"] = request.Amount.Currency,
            ["reason"] = NormalizeRefundReason(request.Reason),
            ["reference"] = new { merchant = request.IdempotencyKey }
        };
        if (options.PostUrl is not null)
            payload["post"] = new { url = options.PostUrl.ToString() };

        ApiResponse api = await SendAsync(HttpMethod.Post, "v2/refunds/", payload, cancellationToken);
        if (!api.IsSuccess)
            return PaymentResult.Failed(api.Error!);

        string refundId = GetString(api.Root, "id") ?? request.IdempotencyKey;
        Payment payment = new()
        {
            Id = request.PaymentId,
            Provider = Name,
            ProviderReference = refundId,
            Amount = request.Amount,
            Status = request.IsPartial ? PaymentStatuses.PartiallyRefunded : PaymentStatuses.Refunded
        };
        return new(payment);
    }

    public Task<PaymentResult> ChargeRecurringAsync(RecurringPaymentRequest request, CancellationToken cancellationToken = default)
    {
        PaymentRequest payment = new()
        {
            Provider = Name,
            Amount = request.Amount,
            IdempotencyKey = request.IdempotencyKey,
            CustomerReference = request.CustomerReference,
            PaymentMethod = request.PaymentMethod,
            Metadata = request.Metadata ?? []
        };
        return CreateAsync(payment, cancellationToken);
    }

    public async Task<Payment?> GetAsync(string paymentId, CancellationToken cancellationToken = default)
    {
        string path = paymentId.StartsWith("auth_", StringComparison.OrdinalIgnoreCase)
            ? $"v2/authorize/{Uri.EscapeDataString(paymentId)}"
            : $"v2/charges/{Uri.EscapeDataString(paymentId)}";
        ApiResponse api = await SendAsync(HttpMethod.Get, path, null, cancellationToken);
        if (!api.IsSuccess)
            return null;

        return MapPayment(api.Root, new Money(GetString(api.Root, "currency") ?? "KWD", GetDecimal(api.Root, "amount") ?? 0m));
    }

    public async Task<PaymentStatuses?> GetStatusAsync(string paymentId, CancellationToken cancellationToken = default)
        => (await GetAsync(paymentId, cancellationToken))?.Status;

    public Task<bool> ValidateWebhookAsync(PaymentWebhookRequest request, CancellationToken cancellationToken = default)
    {
        if (!TryGetHeader(request.Headers, "hashstring", out string? hash))
            return Task.FromResult(false);

        try
        {
            using JsonDocument document = JsonDocument.Parse(request.Body);
            JsonElement root = document.RootElement;
            string currency = GetString(root, "currency") ?? string.Empty;
            string amount = FormatAmount(GetDecimal(root, "amount") ?? 0m, currency);
            JsonElement reference = root.TryGetProperty("reference", out JsonElement referenceElement) ? referenceElement : default;
            JsonElement transaction = root.TryGetProperty("transaction", out JsonElement transactionElement) ? transactionElement : default;
            string value =
                $"x_id{GetString(root, "id")}" +
                $"x_amount{amount}" +
                $"x_currency{currency}" +
                $"x_gateway_reference{GetString(reference, "gateway")}" +
                $"x_payment_reference{GetString(reference, "payment")}" +
                $"x_status{GetString(root, "status")}" +
                $"x_created{GetString(transaction, "created") ?? GetString(root, "created")}";
            byte[] expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(options.SecretKey), Encoding.UTF8.GetBytes(value));
            bool valid = TryDecodeHex(hash!, out byte[] actual) && CryptographicOperations.FixedTimeEquals(expected, actual);
            return Task.FromResult(valid);
        }
        catch (JsonException)
        {
            return Task.FromResult(false);
        }
    }

    public Task<PaymentProviderEvent> ParseWebhookAsync(PaymentWebhookRequest request, CancellationToken cancellationToken = default)
    {
        using JsonDocument document = JsonDocument.Parse(request.Body);
        JsonElement root = document.RootElement;
        string id = GetString(root, "id") ?? Guid.NewGuid().ToString("N");
        string status = GetString(root, "status") ?? "UNKNOWN";
        long createdValue = GetLong(root.TryGetProperty("transaction", out JsonElement transaction) ? transaction : root, "created") ?? 0;
        DateTimeOffset occurredAt = createdValue > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(createdValue)
            : DateTimeOffset.UtcNow;
        Dictionary<string, string> data = new()
        {
            ["object"] = GetString(root, "object") ?? string.Empty,
            ["status"] = status,
            ["currency"] = GetString(root, "currency") ?? string.Empty
        };
        return Task.FromResult(new PaymentProviderEvent(Name, id, $"payment.{status.ToLowerInvariant()}", id, occurredAt, data));
    }

    private Dictionary<string, object?> CreatePaymentPayload(PaymentRequest request)
    {
        Dictionary<string, object?> payload = new()
        {
            ["amount"] = request.Amount.Value,
            ["currency"] = request.Amount.Currency,
            ["threeDSecure"] = true,
            ["save_card"] = request.PaymentMethod is not null,
            ["description"] = request.Description,
            ["source"] = new { id = request.PaymentMethod?.Reference ?? request.Metadata.GetValueOrDefault("source_id") ?? "src_all" },
            ["reference"] = new { transaction = request.IdempotencyKey, order = request.CustomerReference ?? request.IdempotencyKey },
            ["customer"] = CreateCustomer(request),
            ["metadata"] = request.Metadata
        };
        AddMerchant(payload);
        if (options.RedirectUrl is not null)
            payload["redirect"] = new { url = options.RedirectUrl.ToString() };
        if (options.PostUrl is not null)
            payload["post"] = new { url = options.PostUrl.ToString() };
        if (request.Metadata.TryGetValue("statement_descriptor", out string? descriptor))
            payload["statement_descriptor"] = descriptor;
        return payload;
    }

    private object CreateCustomer(PaymentRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.CustomerReference) && request.CustomerReference.StartsWith("cus_", StringComparison.OrdinalIgnoreCase))
            return new { id = request.CustomerReference };

        return new
        {
            first_name = request.Metadata.GetValueOrDefault("first_name") ?? "Guest",
            last_name = request.Metadata.GetValueOrDefault("last_name") ?? "Customer",
            email = request.Metadata.GetValueOrDefault("email") ?? "guest@example.invalid",
            phone = new
            {
                country_code = request.Metadata.GetValueOrDefault("phone_country_code") ?? string.Empty,
                number = request.Metadata.GetValueOrDefault("phone_number") ?? string.Empty
            }
        };
    }

    private void AddMerchant(IDictionary<string, object?> payload)
    {
        if (!string.IsNullOrWhiteSpace(options.MerchantId))
            payload["merchant"] = new { id = options.MerchantId };
    }

    private async Task<PaymentResult> SendPaymentAsync(string path, object payload, Money fallbackAmount, CancellationToken cancellationToken)
    {
        ApiResponse api = await SendAsync(HttpMethod.Post, path, payload, cancellationToken);
        return api.IsSuccess ? new(MapPayment(api.Root, fallbackAmount)) : PaymentResult.Failed(api.Error!);
    }

    private async Task<ApiResponse> SendAsync(HttpMethod method, string path, object? payload, CancellationToken cancellationToken)
    {
        options.Validate();
        using HttpRequestMessage message = new(method, path);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.SecretKey);
        if (payload is not null)
            message.Content = JsonContent.Create(payload);

        using HttpResponseMessage response = await httpClient.SendAsync(message, cancellationToken);
        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        using JsonDocument document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        JsonElement root = document.RootElement.Clone();
        return response.IsSuccessStatusCode
            ? new(root, null)
            : new(root, MapError(root, (int)response.StatusCode));
    }

    private Payment MapPayment(JsonElement root, Money fallbackAmount)
    {
        string id = GetString(root, "id") ?? Guid.NewGuid().ToString("N");
        string statusValue = GetString(root, "status") ?? "PENDING";
        JsonElement transaction = root.TryGetProperty("transaction", out JsonElement transactionElement) ? transactionElement : default;
        Uri? redirect = Uri.TryCreate(GetString(transaction, "url"), UriKind.Absolute, out Uri? parsed) ? parsed : null;
        PaymentStatuses status = statusValue.ToUpperInvariant() switch
        {
            "CAPTURED" => PaymentStatuses.Captured,
            "AUTHORIZED" => PaymentStatuses.Authorized,
            "VOID" => PaymentStatuses.Voided,
            "CANCELLED" or "ABANDONED" or "TIMEDOUT" => PaymentStatuses.Cancelled,
            "FAILED" or "DECLINED" or "RESTRICTED" => PaymentStatuses.Failed,
            "INITIATED" => PaymentStatuses.RequiresAction,
            _ => PaymentStatuses.Pending
        };
        string currency = GetString(root, "currency") ?? fallbackAmount.Currency;
        decimal amount = GetDecimal(root, "amount") ?? fallbackAmount.Value;
        return new()
        {
            Id = id,
            Provider = Name,
            ProviderReference = id,
            CustomerReference = GetNestedString(root, "reference", "order"),
            Amount = new(currency, amount),
            Status = status,
            NextAction = status == PaymentStatuses.RequiresAction ? new(PaymentActionTypes.Redirect, redirect) : null,
            PaymentMethod = GetNestedString(root, "source", "id") is string source
                ? new(Name, source, PaymentMethodTypes.ProviderSpecific)
                : null,
            CreatedAt = ParseCreated(transaction, root)
        };
    }

    private static DateTimeOffset ParseCreated(JsonElement transaction, JsonElement root)
    {
        long created = GetLong(transaction, "created") ?? GetLong(root, "created") ?? 0;
        return created > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(created) : DateTimeOffset.UtcNow;
    }

    private static string NormalizeRefundReason(string? reason) => reason is "duplicate" or "fraudulent" or "requested_by_customer"
        ? reason
        : "requested_by_customer";

    private static string FormatAmount(decimal amount, string currency)
        => amount.ToString(ThreeDecimalCurrencies.Contains(currency) ? "0.000" : "0.00", CultureInfo.InvariantCulture);

    private static PaymentError MapError(JsonElement root, int statusCode)
    {
        string code = statusCode.ToString(CultureInfo.InvariantCulture);
        string message = GetNestedString(root, "response", "message") ?? "Tap request failed.";
        if (root.TryGetProperty("errors", out JsonElement errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
        {
            code = GetString(errors[0], "code") ?? code;
            message = GetString(errors[0], "description") ?? message;
        }
        PaymentErrorTypes type = statusCode switch
        {
            401 or 403 => PaymentErrorTypes.Authentication,
            429 => PaymentErrorTypes.RateLimited,
            >= 500 => PaymentErrorTypes.ProviderUnavailable,
            _ => message.Contains("declin", StringComparison.OrdinalIgnoreCase) ? PaymentErrorTypes.Declined : PaymentErrorTypes.InvalidRequest
        };
        return new(type, code, message, code, statusCode is 429 or >= 500);
    }

    private static PaymentResult Unsupported(string code, string message)
        => PaymentResult.Failed(new(PaymentErrorTypes.Unsupported, code, message));

    private static string? GetString(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out JsonElement value) && value.ValueKind != JsonValueKind.Null
            ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString()
            : null;

    private static string? GetNestedString(JsonElement root, string parent, string property)
        => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(parent, out JsonElement nested) ? GetString(nested, property) : null;

    private static decimal? GetDecimal(JsonElement element, string property)
        => decimal.TryParse(GetString(element, property), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value) ? value : null;

    private static long? GetLong(JsonElement element, string property)
        => long.TryParse(GetString(element, property), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) ? value : null;

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

    private sealed record ApiResponse(JsonElement Root, PaymentError? Error)
    {
        public bool IsSuccess => Error is null;
    }
}