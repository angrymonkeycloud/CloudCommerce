using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AngryMonkey.Cloud.Geography;

namespace AngryMonkey.CloudPayments.PayTabs;

public sealed class PayTabsPaymentProvider(HttpClient httpClient, PayTabsOptions options) : IPaymentProvider
{
    public string Name => "PayTabs";

    public PaymentProviderCapabilities Capabilities =>
        PaymentProviderCapabilities.Create |
        PaymentProviderCapabilities.Authorize |
        PaymentProviderCapabilities.Capture |
        PaymentProviderCapabilities.Void |
        PaymentProviderCapabilities.Refund |
        PaymentProviderCapabilities.PartialRefund |
        PaymentProviderCapabilities.Webhooks;

    public Task<PaymentResult> CreateAsync(PaymentRequest request, CancellationToken cancellationToken = default)
        => CreateHostedPaymentAsync(request, "sale", cancellationToken);

    public Task<PaymentResult> AuthorizeAsync(PaymentRequest request, CancellationToken cancellationToken = default)
        => CreateHostedPaymentAsync(request, "auth", cancellationToken);

    public async Task<PaymentResult> CaptureAsync(CaptureRequest request, CancellationToken cancellationToken = default)
    {
        Money? amount = request.Amount ?? await GetAmountAsync(request.PaymentId, cancellationToken);
        if (amount is null)
            return Invalid("capture_amount_unavailable", "PayTabs capture requires an amount or a queryable authorization.");

        return await FollowUpAsync("capture", request.PaymentId, amount, request.IdempotencyKey, false, cancellationToken);
    }

    public async Task<PaymentResult> VoidAsync(VoidPaymentRequest request, CancellationToken cancellationToken = default)
    {
        Money? amount = await GetAmountAsync(request.PaymentId, cancellationToken);
        if (amount is null)
            return Invalid("void_amount_unavailable", "PayTabs void requires a queryable authorization.");

        return await FollowUpAsync("void", request.PaymentId, amount, request.IdempotencyKey, false, cancellationToken);
    }

    public Task<PaymentResult> RefundAsync(RefundRequest request, CancellationToken cancellationToken = default)
        => FollowUpAsync("refund", request.PaymentId, request.Amount, request.IdempotencyKey, request.IsPartial, cancellationToken);

    public Task<PaymentResult> ChargeRecurringAsync(RecurringPaymentRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(Unsupported("recurring_agreement_required", "PayTabs recurring charges require an application-owned token agreement and are not inferred from a generic payment reference."));

    public async Task<Payment?> GetAsync(string paymentId, CancellationToken cancellationToken = default)
    {
        Dictionary<string, object?> payload = new()
        {
            ["profile_id"] = options.ProfileId,
            ["tran_ref"] = paymentId
        };
        ApiResponse api = await SendAsync(HttpMethod.Post, "payment/query", payload, cancellationToken);
        return api.IsSuccess ? MapPayment(api.Root, null) : null;
    }

    public async Task<PaymentStatuses?> GetStatusAsync(string paymentId, CancellationToken cancellationToken = default)
        => (await GetAsync(paymentId, cancellationToken))?.Status;

    public Task<bool> ValidateWebhookAsync(PaymentWebhookRequest request, CancellationToken cancellationToken = default)
    {
        if (!TryGetHeader(request.Headers, "Signature", out string? signature))
            return Task.FromResult(false);

        byte[] expected = HMACSHA256.HashData(options.ServerKeyBytes(), request.Body.Span);
        bool valid = TryDecodeHex(signature!, out byte[] actual) && CryptographicOperations.FixedTimeEquals(expected, actual);
        return Task.FromResult(valid);
    }

    public Task<PaymentProviderEvent> ParseWebhookAsync(PaymentWebhookRequest request, CancellationToken cancellationToken = default)
    {
        using JsonDocument document = JsonDocument.Parse(request.Body);
        JsonElement root = document.RootElement;
        string transactionReference = GetString(root, "tran_ref") ?? Guid.NewGuid().ToString("N");
        string transactionType = GetString(root, "tran_type") ?? "payment";
        string? cartId = GetString(root, "cart_id");
        Dictionary<string, string> data = new()
        {
            ["transactionType"] = transactionType,
            ["cartId"] = cartId ?? string.Empty,
            ["responseStatus"] = GetNestedString(root, "payment_result", "response_status") ?? string.Empty,
            ["responseMessage"] = GetNestedString(root, "payment_result", "response_message") ?? string.Empty
        };
        return Task.FromResult(new PaymentProviderEvent(Name, transactionReference, $"payment.{transactionType.ToLowerInvariant()}", transactionReference, DateTimeOffset.UtcNow, data));
    }

    private async Task<PaymentResult> CreateHostedPaymentAsync(PaymentRequest request, string transactionType, CancellationToken cancellationToken)
    {
        Dictionary<string, object?> payload = new()
        {
            ["profile_id"] = options.ProfileId,
            ["tran_type"] = transactionType,
            ["tran_class"] = "ecom",
            ["cart_id"] = request.IdempotencyKey,
            ["cart_currency"] = request.Amount.Currency,
            ["cart_amount"] = request.Amount.Value,
            ["cart_description"] = request.Description ?? request.IdempotencyKey,
            ["paypage_lang"] = options.Language
        };
        if (options.ReturnUrl is not null)
            payload["return"] = options.ReturnUrl.ToString();
        if (options.CallbackUrl is not null)
            payload["callback"] = options.CallbackUrl.ToString();

        Dictionary<string, string?> customer = new()
        {
            ["name"] = request.Metadata.GetValueOrDefault("customer_name"),
            ["email"] = request.Metadata.GetValueOrDefault("customer_email"),
            ["phone"] = request.Metadata.GetValueOrDefault("customer_phone"),
            ["street1"] = request.Metadata.GetValueOrDefault("street"),
            ["city"] = request.Metadata.GetValueOrDefault("city"),
            ["state"] = request.Metadata.GetValueOrDefault("state"),
            ["country"] = request.Metadata.GetValueOrDefault("country"),
            ["zip"] = request.Metadata.GetValueOrDefault("postal_code")
        };
        if (customer.Values.Any(value => !string.IsNullOrWhiteSpace(value)))
        {
            payload["customer_details"] = customer;
            payload["shipping_details"] = customer;
        }

        ApiResponse api = await SendAsync(HttpMethod.Post, "payment/request", payload, cancellationToken);
        if (!api.IsSuccess)
            return PaymentResult.Failed(api.Error!);

        return new(MapPayment(api.Root, request.Amount));
    }

    private async Task<PaymentResult> FollowUpAsync(string transactionType, string previousReference, Money amount, string? idempotencyKey, bool isPartial, CancellationToken cancellationToken)
    {
        Dictionary<string, object?> payload = new()
        {
            ["profile_id"] = options.ProfileId,
            ["tran_type"] = transactionType,
            ["tran_class"] = "ecom",
            ["tran_ref"] = previousReference,
            ["cart_id"] = idempotencyKey ?? $"{transactionType}-{Guid.NewGuid():N}",
            ["cart_currency"] = amount.Currency,
            ["cart_amount"] = amount.Value,
            ["cart_description"] = $"CloudPayments {transactionType}"
        };
        ApiResponse api = await SendAsync(HttpMethod.Post, "payment/request", payload, cancellationToken);
        if (!api.IsSuccess)
            return PaymentResult.Failed(api.Error!);

        Payment mapped = MapPayment(api.Root, amount);
        PaymentStatuses targetStatus = transactionType switch
        {
            "capture" => PaymentStatuses.Captured,
            "void" => PaymentStatuses.Voided,
            "refund" when isPartial => PaymentStatuses.PartiallyRefunded,
            "refund" => PaymentStatuses.Refunded,
            _ => mapped.Status
        };
        return new(new()
        {
            Id = previousReference,
            Provider = Name,
            ProviderReference = mapped.Id,
            Amount = amount,
            Status = targetStatus,
            CreatedAt = mapped.CreatedAt
        });
    }

    private async Task<Money?> GetAmountAsync(string paymentId, CancellationToken cancellationToken)
        => (await GetAsync(paymentId, cancellationToken))?.Amount;

    private async Task<ApiResponse> SendAsync(HttpMethod method, string path, object? payload, CancellationToken cancellationToken)
    {
        options.Validate();
        using HttpRequestMessage message = new(method, path);
        message.Headers.TryAddWithoutValidation("authorization", options.ServerKey);
        if (payload is not null)
            message.Content = JsonContent.Create(payload);

        using HttpResponseMessage response = await httpClient.SendAsync(message, cancellationToken);
        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        using JsonDocument document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        JsonElement root = document.RootElement.Clone();
        bool providerError = root.TryGetProperty("code", out JsonElement codeElement) &&
            codeElement.ValueKind != JsonValueKind.Null &&
            GetString(root, "tran_ref") is null &&
            GetString(root, "redirect_url") is null;
        return response.IsSuccessStatusCode && !providerError
            ? new(root, null)
            : new(root, MapError(root, (int)response.StatusCode));
    }

    private Payment MapPayment(JsonElement root, Money? fallbackAmount)
    {
        string id = GetString(root, "tran_ref") ?? Guid.NewGuid().ToString("N");
        string transactionType = GetString(root, "tran_type") ?? "sale";
        string? responseStatus = GetNestedString(root, "payment_result", "response_status");
        Uri? redirect = Uri.TryCreate(GetString(root, "redirect_url"), UriKind.Absolute, out Uri? parsed) ? parsed : null;
        PaymentStatuses status = redirect is not null
            ? PaymentStatuses.RequiresAction
            : responseStatus?.ToUpperInvariant() switch
            {
                "A" => transactionType.ToLowerInvariant() switch
                {
                    "auth" => PaymentStatuses.Authorized,
                    "refund" => PaymentStatuses.Refunded,
                    "void" or "release" => PaymentStatuses.Voided,
                    _ => PaymentStatuses.Captured
                },
                "D" or "E" => PaymentStatuses.Failed,
                _ => PaymentStatuses.Pending
            };
        string currency = GetString(root, "cart_currency") ?? fallbackAmount?.Currency ?? "USD";
        decimal amount = GetDecimal(root, "cart_amount") ?? fallbackAmount?.Value ?? 0m;
        return new()
        {
            Id = id,
            Provider = Name,
            ProviderReference = id,
            CustomerReference = GetString(root, "cart_id"),
            Amount = new(currency, amount),
            Status = status,
            NextAction = status == PaymentStatuses.RequiresAction ? new(PaymentActionTypes.Redirect, redirect) : null
        };
    }

    private static PaymentError MapError(JsonElement root, int statusCode)
    {
        string code = GetString(root, "code") ?? statusCode.ToString(CultureInfo.InvariantCulture);
        string message = GetString(root, "message") ?? GetNestedString(root, "payment_result", "response_message") ?? "PayTabs request failed.";
        PaymentErrorTypes type = statusCode switch
        {
            401 or 403 => PaymentErrorTypes.Authentication,
            429 => PaymentErrorTypes.RateLimited,
            >= 500 => PaymentErrorTypes.ProviderUnavailable,
            _ => message.Contains("declin", StringComparison.OrdinalIgnoreCase) ? PaymentErrorTypes.Declined : PaymentErrorTypes.InvalidRequest
        };
        return new(type, code, message, code, statusCode is 429 or >= 500);
    }

    private static PaymentResult Invalid(string code, string message)
        => PaymentResult.Failed(new(PaymentErrorTypes.InvalidRequest, code, message));

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

file static class PayTabsOptionsExtensions
{
    public static byte[] ServerKeyBytes(this PayTabsOptions options) => Encoding.UTF8.GetBytes(options.ServerKey);
}