using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AngryMonkey.Cloud.Geography;

namespace AngryMonkey.CloudPayments.MyFatoorah;

public sealed class MyFatoorahPaymentProvider(HttpClient httpClient, MyFatoorahOptions options) : IPaymentProvider
{
    public string Name => "MyFatoorah";

    public PaymentProviderCapabilities Capabilities =>
        PaymentProviderCapabilities.Create |
        PaymentProviderCapabilities.Authorize |
        PaymentProviderCapabilities.Capture |
        PaymentProviderCapabilities.Void |
        PaymentProviderCapabilities.Refund |
        PaymentProviderCapabilities.PartialRefund |
        PaymentProviderCapabilities.Webhooks;

    public Task<PaymentResult> CreateAsync(PaymentRequest request, CancellationToken cancellationToken = default)
        => CreateInvoiceAsync(request, true, cancellationToken);

    public Task<PaymentResult> AuthorizeAsync(PaymentRequest request, CancellationToken cancellationToken = default)
        => CreateInvoiceAsync(request, false, cancellationToken);

    public async Task<PaymentResult> CaptureAsync(CaptureRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Amount is null)
            return Unsupported("capture_amount_required", "MyFatoorah capture requires the authorized amount.");

        return await UpdatePaymentAsync(request.PaymentId, "CAPTURE", request.Amount, cancellationToken);
    }

    public Task<PaymentResult> VoidAsync(VoidPaymentRequest request, CancellationToken cancellationToken = default)
        => UpdatePaymentAsync(request.PaymentId, "RELEASE", null, cancellationToken);

    public async Task<PaymentResult> RefundAsync(RefundRequest request, CancellationToken cancellationToken = default)
    {
        Dictionary<string, object?> payload = new()
        {
            ["KeyType"] = "InvoiceId",
            ["Key"] = request.PaymentId,
            ["ServiceChargeOnCustomer"] = false,
            ["Amount"] = request.Amount.Value,
            ["Comment"] = request.Reason ?? "CloudPayments refund",
            ["ExternalIdentifier"] = request.IdempotencyKey
        };

        ApiResponse api = await SendAsync(HttpMethod.Post, "v2/MakeRefund", payload, cancellationToken);
        if (!api.IsSuccess)
            return PaymentResult.Failed(api.Error!);

        JsonElement data = GetData(api.Root);
        string? reference = GetString(data, "RefundReference") ?? GetString(data, "RefundId");
        Payment payment = new()
        {
            Id = request.PaymentId,
            Provider = Name,
            ProviderReference = reference,
            Amount = request.Amount,
            Status = PaymentStatuses.Pending,
            Metadata = new Dictionary<string, string>
            {
                ["operation"] = request.IsPartial ? "partial_refund_requested" : "refund_requested"
            }
        };
        return new(payment);
    }

    public Task<PaymentResult> ChargeRecurringAsync(RecurringPaymentRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(Unsupported("recurring_not_configured", "Use an application-owned MyFatoorah recurring agreement before charging recurring payments."));

    public async Task<Payment?> GetAsync(string paymentId, CancellationToken cancellationToken = default)
    {
        ApiResponse api = await SendAsync(HttpMethod.Get, $"v3/invoices/{Uri.EscapeDataString(paymentId)}", null, cancellationToken);
        return api.IsSuccess ? MapDetailedPayment(GetData(api.Root), paymentId) : null;
    }

    public async Task<PaymentStatuses?> GetStatusAsync(string paymentId, CancellationToken cancellationToken = default)
        => (await GetAsync(paymentId, cancellationToken))?.Status;

    public Task<bool> ValidateWebhookAsync(PaymentWebhookRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(options.WebhookSecret) || !TryGetHeader(request.Headers, "myfatoorah-signature", out string? signature))
            return Task.FromResult(false);

        try
        {
            using JsonDocument document = JsonDocument.Parse(request.Body);
            JsonElement data = document.RootElement.GetProperty("Data");
            JsonElement invoice = data.GetProperty("Invoice");
            JsonElement transaction = data.GetProperty("Transaction");
            string signedValue = string.Join(',',
                $"Invoice.Id={GetString(invoice, "Id") ?? string.Empty}",
                $"Invoice.Status={GetString(invoice, "Status") ?? string.Empty}",
                $"Transaction.Status={GetString(transaction, "Status") ?? string.Empty}",
                $"Transaction.PaymentId={GetString(transaction, "PaymentId") ?? string.Empty}",
                $"Invoice.ExternalIdentifier={GetString(invoice, "ExternalIdentifier") ?? string.Empty}");
            byte[] expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(options.WebhookSecret), Encoding.UTF8.GetBytes(signedValue));
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
        JsonElement eventElement = root.GetProperty("Event");
        JsonElement data = root.GetProperty("Data");
        JsonElement invoice = data.GetProperty("Invoice");
        JsonElement transaction = data.GetProperty("Transaction");
        string eventId = GetString(eventElement, "Reference") ?? Guid.NewGuid().ToString("N");
        string eventType = GetString(eventElement, "Name") ?? "PAYMENT_STATUS_CHANGED";
        string? paymentId = GetString(transaction, "PaymentId") ?? GetString(invoice, "Id");
        DateTimeOffset occurredAt = DateTimeOffset.TryParse(GetString(eventElement, "CreationDate"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed)
            ? parsed
            : DateTimeOffset.UtcNow;
        Dictionary<string, string> values = new()
        {
            ["invoiceId"] = GetString(invoice, "Id") ?? string.Empty,
            ["invoiceStatus"] = GetString(invoice, "Status") ?? string.Empty,
            ["transactionStatus"] = GetString(transaction, "Status") ?? string.Empty
        };
        return Task.FromResult(new PaymentProviderEvent(Name, eventId, eventType, paymentId, occurredAt, values));
    }

    private async Task<PaymentResult> CreateInvoiceAsync(PaymentRequest request, bool autoCapture, CancellationToken cancellationToken)
    {
        Dictionary<string, object?> payload = new()
        {
            ["InvoiceValue"] = request.Amount.Value,
            ["PaymentMethodId"] = options.PaymentMethodId,
            ["DisplayCurrencyIso"] = request.Amount.Currency,
            ["Language"] = options.Language,
            ["CustomerReference"] = request.CustomerReference ?? request.IdempotencyKey,
            ["UserDefinedField"] = request.IdempotencyKey,
            ["ProcessingDetails"] = new Dictionary<string, object?>
            {
                ["AutoCapture"] = autoCapture,
                ["Bypass3DS"] = false
            }
        };
        AddIfPresent(payload, "CustomerName", request.Metadata, "customer_name");
        AddIfPresent(payload, "CustomerEmail", request.Metadata, "customer_email");
        AddIfPresent(payload, "CustomerMobile", request.Metadata, "customer_mobile");
        if (options.ReturnUrl is not null)
            payload["CallBackUrl"] = options.ReturnUrl.ToString();
        if (options.ErrorUrl is not null)
            payload["ErrorUrl"] = options.ErrorUrl.ToString();

        ApiResponse api = await SendAsync(HttpMethod.Post, "v2/ExecutePayment", payload, cancellationToken);
        if (!api.IsSuccess)
            return PaymentResult.Failed(api.Error!);

        JsonElement data = GetData(api.Root);
        string invoiceId = GetString(data, "InvoiceId") ?? request.IdempotencyKey;
        Uri? paymentUrl = Uri.TryCreate(GetString(data, "PaymentURL"), UriKind.Absolute, out Uri? parsed) ? parsed : null;
        Payment payment = new()
        {
            Id = invoiceId,
            Provider = Name,
            ProviderReference = invoiceId,
            CustomerReference = request.CustomerReference,
            Amount = request.Amount,
            Status = PaymentStatuses.RequiresAction,
            NextAction = new(PaymentActionTypes.Redirect, paymentUrl),
            Metadata = new Dictionary<string, string>(request.Metadata)
        };
        return new(payment);
    }

    private async Task<PaymentResult> UpdatePaymentAsync(string paymentId, string operation, Money? amount, CancellationToken cancellationToken)
    {
        Dictionary<string, object?> payload = new() { ["OperationType"] = operation };
        if (amount is not null)
            payload["Amount"] = amount.Value;

        ApiResponse api = await SendAsync(HttpMethod.Put, $"v3/payments/{Uri.EscapeDataString(paymentId)}", payload, cancellationToken);
        if (!api.IsSuccess)
            return PaymentResult.Failed(api.Error!);

        Payment payment = MapDetailedPayment(GetData(api.Root), paymentId);
        return new(CloneWithStatus(payment, operation == "RELEASE" ? PaymentStatuses.Voided : PaymentStatuses.Captured, amount));
    }

    private async Task<ApiResponse> SendAsync(HttpMethod method, string path, object? payload, CancellationToken cancellationToken)
    {
        options.Validate();
        using HttpRequestMessage message = new(method, path);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiToken);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (payload is not null)
            message.Content = JsonContent.Create(payload);

        using HttpResponseMessage response = await httpClient.SendAsync(message, cancellationToken);
        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        using JsonDocument document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        JsonElement root = document.RootElement.Clone();
        bool providerSuccess = !root.TryGetProperty("IsSuccess", out JsonElement success) || success.ValueKind != JsonValueKind.False;
        return response.IsSuccessStatusCode && providerSuccess
            ? new(root, null)
            : new(root, MapError(root, (int)response.StatusCode));
    }

    private Payment MapDetailedPayment(JsonElement data, string fallbackId)
    {
        JsonElement invoice = data.TryGetProperty("Invoice", out JsonElement invoiceElement) ? invoiceElement : data;
        JsonElement transaction = data.TryGetProperty("Transaction", out JsonElement transactionElement) ? transactionElement : default;
        JsonElement amount = data.TryGetProperty("Amount", out JsonElement amountElement) ? amountElement : default;
        string id = GetString(invoice, "Id") ?? fallbackId;
        string invoiceStatus = GetString(invoice, "Status") ?? "PENDING";
        string transactionStatus = transaction.ValueKind == JsonValueKind.Object ? GetString(transaction, "Status") ?? string.Empty : string.Empty;
        string currency = amount.ValueKind == JsonValueKind.Object
            ? GetString(amount, "DisplayCurrency") ?? GetString(amount, "BaseCurrency") ?? "KWD"
            : "KWD";
        decimal value = amount.ValueKind == JsonValueKind.Object
            ? GetDecimal(amount, "ValueInDisplayCurrency") ?? GetDecimal(amount, "ValueInBaseCurrency") ?? 0m
            : 0m;
        return new()
        {
            Id = id,
            Provider = Name,
            ProviderReference = transaction.ValueKind == JsonValueKind.Object ? GetString(transaction, "PaymentId") ?? id : id,
            Amount = new(currency, value),
            Status = MapStatus(invoiceStatus, transactionStatus),
            CreatedAt = DateTimeOffset.TryParse(GetString(invoice, "CreationDate"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset created)
                ? created
                : DateTimeOffset.UtcNow
        };
    }

    private static Payment CloneWithStatus(Payment payment, PaymentStatuses status, Money? amount) => new()
    {
        Id = payment.Id,
        Provider = payment.Provider,
        ProviderReference = payment.ProviderReference,
        CustomerReference = payment.CustomerReference,
        Amount = amount ?? payment.Amount,
        Status = status,
        CreatedAt = payment.CreatedAt,
        UpdatedAt = DateTimeOffset.UtcNow,
        Metadata = payment.Metadata
    };

    private static PaymentStatuses MapStatus(string invoiceStatus, string transactionStatus) => transactionStatus.ToUpperInvariant() switch
    {
        "SUCCESS" => PaymentStatuses.Captured,
        "AUTHORIZE" => PaymentStatuses.Authorized,
        "FAILED" => PaymentStatuses.Failed,
        "CANCELED" => PaymentStatuses.Cancelled,
        _ => invoiceStatus.ToUpperInvariant() switch
        {
            "PAID" => PaymentStatuses.Captured,
            "EXPIRED" => PaymentStatuses.Cancelled,
            _ => PaymentStatuses.Pending
        }
    };

    private static PaymentError MapError(JsonElement root, int statusCode)
    {
        string code = statusCode.ToString(CultureInfo.InvariantCulture);
        if (root.TryGetProperty("ValidationErrors", out JsonElement errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
            code = GetString(errors[0], "Name") ?? code;
        string message = GetString(root, "Message") ?? "MyFatoorah request failed.";
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

    private static JsonElement GetData(JsonElement root)
        => root.TryGetProperty("Data", out JsonElement data) ? data : root;

    private static string? GetString(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out JsonElement value))
            return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static decimal? GetDecimal(JsonElement element, string property)
    {
        string? value = GetString(element, property);
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed) ? parsed : null;
    }

    private static void AddIfPresent(IDictionary<string, object?> payload, string providerKey, IReadOnlyDictionary<string, string> metadata, string metadataKey)
    {
        if (metadata.TryGetValue(metadataKey, out string? value) && !string.IsNullOrWhiteSpace(value))
            payload[providerKey] = value;
    }

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

    private sealed record ApiResponse(JsonElement Root, PaymentError? Error)
    {
        public bool IsSuccess => Error is null;
    }
}