using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AngryMonkey.Cloud.Geography;

namespace AngryMonkey.CloudPayments.SkipCash;

public sealed class SkipCashPaymentProvider(HttpClient httpClient, SkipCashOptions options) : IPaymentProvider
{
    private static readonly string[] CustomerMetadataKeys =
    [
        "first_name", "last_name", "phone", "email", "street", "city", "state", "country", "postal_code"
    ];

    public string Name => "SkipCash";

    public PaymentProviderCapabilities Capabilities =>
        PaymentProviderCapabilities.Create |
        PaymentProviderCapabilities.Webhooks;

    public async Task<PaymentResult> CreateAsync(PaymentRequest request, CancellationToken cancellationToken = default)
    {
        if (!request.Amount.Currency.Equals("QAR", StringComparison.OrdinalIgnoreCase))
            return Invalid("currency_not_supported", "SkipCash processes payments in QAR.");

        string? missing = CustomerMetadataKeys.FirstOrDefault(key => !request.Metadata.TryGetValue(key, out string? value) || string.IsNullOrWhiteSpace(value));
        if (missing is not null)
            return Invalid("missing_customer_metadata", $"SkipCash requires payment metadata '{missing}'.");

        options.Validate();
        string amount = request.Amount.Value.ToString("0.00", CultureInfo.InvariantCulture);
        string transactionId = GetTransactionId(request);
        Dictionary<string, string?> payload = new()
        {
            ["uid"] = Guid.NewGuid().ToString(),
            ["keyId"] = options.KeyId,
            ["amount"] = amount,
            ["firstName"] = request.Metadata["first_name"],
            ["lastName"] = request.Metadata["last_name"],
            ["phone"] = request.Metadata["phone"],
            ["email"] = request.Metadata["email"],
            ["street"] = request.Metadata["street"],
            ["city"] = request.Metadata["city"],
            ["state"] = request.Metadata["state"],
            ["country"] = request.Metadata["country"],
            ["postalCode"] = request.Metadata["postal_code"],
            ["transactionId"] = transactionId,
            ["custom1"] = request.CustomerReference
        };

        string signedValue = JoinForSigning(payload,
            ("uid", "Uid"), ("keyId", "KeyId"), ("amount", "Amount"), ("firstName", "FirstName"),
            ("lastName", "LastName"), ("phone", "Phone"), ("email", "Email"), ("street", "Street"),
            ("city", "City"), ("state", "State"), ("country", "Country"), ("postalCode", "PostalCode"),
            ("transactionId", "TransactionId"), ("custom1", "Custom1"));
        string signature = Sign(signedValue, options.KeySecret);

        using HttpRequestMessage message = new(HttpMethod.Post, "api/v1/payments");
        message.Headers.TryAddWithoutValidation("Authorization", signature);
        message.Content = JsonContent.Create(payload);
        return await SendAndMapAsync(message, request.Amount, cancellationToken);
    }

    public Task<PaymentResult> AuthorizeAsync(PaymentRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(Unsupported("authorization_not_supported", "SkipCash online payments are captured by the hosted payment flow."));

    public Task<PaymentResult> CaptureAsync(CaptureRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(Unsupported("capture_not_supported", "SkipCash does not expose capture through this online payment API."));

    public Task<PaymentResult> VoidAsync(VoidPaymentRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(Unsupported("void_not_supported", "SkipCash does not expose void through this online payment API."));

    public Task<PaymentResult> RefundAsync(RefundRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(Unsupported("refund_not_supported", "Use the SkipCash merchant workflow for refunds."));

    public Task<PaymentResult> ChargeRecurringAsync(RecurringPaymentRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(Unsupported("recurring_not_supported", "SkipCash recurring execution is not part of this adapter."));

    public async Task<Payment?> GetAsync(string paymentId, CancellationToken cancellationToken = default)
    {
        options.Validate();
        using HttpRequestMessage message = new(HttpMethod.Get, $"api/v1/payments/{Uri.EscapeDataString(paymentId)}");
        message.Headers.TryAddWithoutValidation("Authorization", options.ClientId);
        using HttpResponseMessage response = await httpClient.SendAsync(message, cancellationToken);
        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement result = GetResult(document.RootElement);
        return MapPayment(result, new Money("QAR", GetDecimal(result, "amount") ?? 0m));
    }

    public async Task<PaymentStatuses?> GetStatusAsync(string paymentId, CancellationToken cancellationToken = default)
        => (await GetAsync(paymentId, cancellationToken))?.Status;

    public Task<bool> ValidateWebhookAsync(PaymentWebhookRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(options.WebhookKey) || !TryGetHeader(request.Headers, "Authorization", out string? signature))
            return Task.FromResult(false);

        try
        {
            using JsonDocument document = JsonDocument.Parse(request.Body);
            JsonElement root = document.RootElement;
            Dictionary<string, string?> values = new()
            {
                ["paymentId"] = GetString(root, "paymentId"),
                ["amount"] = GetValueText(root, "amount"),
                ["statusId"] = GetValueText(root, "statusId"),
                ["transactionId"] = GetString(root, "transactionId"),
                ["custom1"] = GetString(root, "custom1"),
                ["visaId"] = GetString(root, "visaId")
            };
            string signedValue = JoinForSigning(values,
                ("paymentId", "PaymentId"), ("amount", "Amount"), ("statusId", "StatusId"),
                ("transactionId", "TransactionId"), ("custom1", "Custom1"), ("visaId", "VisaId"));
            byte[] expected = Convert.FromBase64String(Sign(signedValue, options.WebhookKey));
            bool valid = TryDecodeBase64(signature!, out byte[] actual) && CryptographicOperations.FixedTimeEquals(expected, actual);
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
        string paymentId = GetString(root, "paymentId") ?? Guid.NewGuid().ToString();
        string status = GetValueText(root, "statusId") ?? "0";
        Dictionary<string, string> data = new()
        {
            ["statusId"] = status,
            ["transactionId"] = GetString(root, "transactionId") ?? string.Empty,
            ["visaId"] = GetString(root, "visaId") ?? string.Empty
        };
        return Task.FromResult(new PaymentProviderEvent(Name, $"{paymentId}:{status}", $"payment.status.{status}", paymentId, DateTimeOffset.UtcNow, data));
    }

    private async Task<PaymentResult> SendAndMapAsync(HttpRequestMessage message, Money fallbackAmount, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await httpClient.SendAsync(message, cancellationToken);
        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        using JsonDocument document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        if (!response.IsSuccessStatusCode)
            return PaymentResult.Failed(MapError(document.RootElement, (int)response.StatusCode));

        JsonElement result = GetResult(document.RootElement);
        if (GetString(result, "id") is null)
            return PaymentResult.Failed(MapError(document.RootElement, (int)response.StatusCode));

        return new(MapPayment(result, fallbackAmount));
    }

    private Payment MapPayment(JsonElement result, Money fallbackAmount)
    {
        string id = GetString(result, "id")!;
        int statusId = GetInt(result, "statusId") ?? 0;
        Uri? payUrl = Uri.TryCreate(GetString(result, "payUrl"), UriKind.Absolute, out Uri? parsed) ? parsed : null;
        Money amount = new(GetString(result, "currency") ?? fallbackAmount.Currency, GetDecimal(result, "amount") ?? fallbackAmount.Value);
        PaymentStatuses status = statusId switch
        {
            1 => PaymentStatuses.Pending,
            2 => PaymentStatuses.Captured,
            3 => PaymentStatuses.Cancelled,
            4 or 5 or 8 => PaymentStatuses.Failed,
            6 => PaymentStatuses.Refunded,
            7 => PaymentStatuses.Pending,
            _ => payUrl is not null ? PaymentStatuses.RequiresAction : PaymentStatuses.Pending
        };
        return new()
        {
            Id = id,
            Provider = Name,
            ProviderReference = GetString(result, "visaId") ?? id,
            CustomerReference = GetString(result, "transactionId"),
            Amount = amount,
            Status = status,
            NextAction = status == PaymentStatuses.RequiresAction ? new(PaymentActionTypes.Redirect, payUrl) : null,
            CreatedAt = DateTimeOffset.TryParse(GetString(result, "created"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset created)
                ? created
                : DateTimeOffset.UtcNow
        };
    }

    private static string GetTransactionId(PaymentRequest request)
    {
        string transactionId = request.Metadata.GetValueOrDefault("transaction_id") ?? request.IdempotencyKey;
        return transactionId.Length <= 40 ? transactionId : transactionId[..40];
    }

    private static string JoinForSigning(IReadOnlyDictionary<string, string?> values, params (string Key, string SignedName)[] keys)
        => string.Join(',', keys
            .Where(item => values.TryGetValue(item.Key, out string? value) && !string.IsNullOrWhiteSpace(value))
            .Select(item => $"{item.SignedName}={values[item.Key]}"));

    private static string Sign(string value, string secret)
        => Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(value)));

    private static JsonElement GetResult(JsonElement root)
        => root.TryGetProperty("resultObj", out JsonElement result) ? result : root;

    private static PaymentError MapError(JsonElement root, int statusCode)
    {
        string message = GetString(root, "returnMessage") ?? GetString(root, "message") ?? "SkipCash request failed.";
        PaymentErrorTypes type = statusCode switch
        {
            401 or 403 => PaymentErrorTypes.Authentication,
            429 => PaymentErrorTypes.RateLimited,
            >= 500 => PaymentErrorTypes.ProviderUnavailable,
            _ => PaymentErrorTypes.InvalidRequest
        };
        return new(type, statusCode.ToString(CultureInfo.InvariantCulture), message, IsRetryable: statusCode is 429 or >= 500);
    }

    private static PaymentResult Invalid(string code, string message)
        => PaymentResult.Failed(new(PaymentErrorTypes.InvalidRequest, code, message));

    private static PaymentResult Unsupported(string code, string message)
        => PaymentResult.Failed(new(PaymentErrorTypes.Unsupported, code, message));

    private static string? GetString(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out JsonElement value) && value.ValueKind != JsonValueKind.Null
            ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString()
            : null;

    private static string? GetValueText(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out JsonElement value) && value.ValueKind != JsonValueKind.Null
            ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText()
            : null;

    private static decimal? GetDecimal(JsonElement element, string property)
        => decimal.TryParse(GetValueText(element, property), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value) ? value : null;

    private static int? GetInt(JsonElement element, string property)
        => int.TryParse(GetValueText(element, property), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : null;

    private static bool TryGetHeader(IReadOnlyDictionary<string, string> headers, string name, out string? value)
    {
        KeyValuePair<string, string> header = headers.FirstOrDefault(pair => pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase));
        value = header.Value;
        return value is not null;
    }

    private static bool TryDecodeBase64(string value, out byte[] bytes)
    {
        try { bytes = Convert.FromBase64String(value); return true; }
        catch (FormatException) { bytes = []; return false; }
    }
}