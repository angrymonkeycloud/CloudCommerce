using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AngryMonkey.Cloud.Geography;

namespace AngryMonkey.CloudPayments.Adyen;

public sealed class AdyenPaymentProvider(HttpClient httpClient, AdyenOptions options) : IPaymentProvider
{
    public string Name => "Adyen";

    public PaymentProviderCapabilities Capabilities => PaymentProviderCapabilities.Create | PaymentProviderCapabilities.Authorize | PaymentProviderCapabilities.Capture | PaymentProviderCapabilities.Void | PaymentProviderCapabilities.Refund | PaymentProviderCapabilities.PartialRefund | PaymentProviderCapabilities.SavedPaymentMethods | PaymentProviderCapabilities.RecurringPayments | PaymentProviderCapabilities.Webhooks;

    public Task<PaymentResult> CreateAsync(PaymentRequest request, CancellationToken cancellationToken = default)
        => CreatePaymentAsync(request, false, cancellationToken);

    public Task<PaymentResult> AuthorizeAsync(PaymentRequest request, CancellationToken cancellationToken = default)
        => CreatePaymentAsync(request, true, cancellationToken);

    public Task<PaymentResult> CaptureAsync(CaptureRequest request, CancellationToken cancellationToken = default)
    {
        object body = new
        {
            merchantAccount = options.MerchantAccount,
            amount = request.Amount is null ? null : Amount(request.Amount),
            reference = request.IdempotencyKey ?? $"capture-{request.PaymentId}"
        };
        return SendModificationAsync($"payments/{Uri.EscapeDataString(request.PaymentId)}/captures", body, request, PaymentStatuses.Captured, cancellationToken);
    }

    public Task<PaymentResult> VoidAsync(VoidPaymentRequest request, CancellationToken cancellationToken = default)
    {
        object body = new { merchantAccount = options.MerchantAccount, reference = request.IdempotencyKey ?? $"cancel-{request.PaymentId}" };
        return SendModificationAsync($"payments/{Uri.EscapeDataString(request.PaymentId)}/cancels", body, request.PaymentId, new Money("USD", 0m), PaymentStatuses.Voided, cancellationToken);
    }

    public Task<PaymentResult> RefundAsync(RefundRequest request, CancellationToken cancellationToken = default)
    {
        object body = new { merchantAccount = options.MerchantAccount, amount = Amount(request.Amount), reference = request.IdempotencyKey };
        return SendModificationAsync($"payments/{Uri.EscapeDataString(request.PaymentId)}/refunds", body, request.PaymentId, request.Amount, request.IsPartial ? PaymentStatuses.PartiallyRefunded : PaymentStatuses.Refunded, cancellationToken);
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
        return CreatePaymentAsync(paymentRequest, false, cancellationToken, "Subscription");
    }

    public Task<Payment?> GetAsync(string paymentId, CancellationToken cancellationToken = default) => Task.FromResult<Payment?>(null);

    public Task<PaymentStatuses?> GetStatusAsync(string paymentId, CancellationToken cancellationToken = default) => Task.FromResult<PaymentStatuses?>(null);

    public Task<bool> ValidateWebhookAsync(PaymentWebhookRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(options.HmacKey))
            return Task.FromResult(false);

        try
        {
            using JsonDocument document = JsonDocument.Parse(request.Body);
            JsonElement item = document.RootElement.GetProperty("notificationItems")[0].GetProperty("NotificationRequestItem");
            JsonElement additionalData = item.GetProperty("additionalData");
            string supplied = additionalData.GetProperty("hmacSignature").GetString()!;
            string payload = string.Join(':',
                Escape(item.GetProperty("pspReference").GetString()),
                Escape(item.TryGetProperty("originalReference", out JsonElement original) ? original.GetString() : string.Empty),
                Escape(item.GetProperty("merchantAccountCode").GetString()),
                Escape(item.GetProperty("merchantReference").GetString()),
                item.GetProperty("amount").GetProperty("value").GetInt64().ToString(CultureInfo.InvariantCulture),
                Escape(item.GetProperty("amount").GetProperty("currency").GetString()),
                Escape(item.GetProperty("eventCode").GetString()),
                Escape(item.GetProperty("success").GetString()));

            byte[] key = Convert.FromHexString(options.HmacKey);
            string expected = Convert.ToBase64String(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload)));
            bool valid = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(supplied));
            return Task.FromResult(valid);
        }
        catch (Exception exception) when (exception is JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
        {
            return Task.FromResult(false);
        }
    }

    public Task<PaymentProviderEvent> ParseWebhookAsync(PaymentWebhookRequest request, CancellationToken cancellationToken = default)
    {
        using JsonDocument document = JsonDocument.Parse(request.Body);
        JsonElement item = document.RootElement.GetProperty("notificationItems")[0].GetProperty("NotificationRequestItem");
        string eventId = item.GetProperty("pspReference").GetString()!;
        string eventType = item.GetProperty("eventCode").GetString()!;
        Dictionary<string, string> data = new()
        {
            ["merchantReference"] = item.GetProperty("merchantReference").GetString() ?? string.Empty,
            ["success"] = item.GetProperty("success").GetString() ?? "false"
        };
        if (item.TryGetProperty("reason", out JsonElement reason))
            data["reason"] = reason.GetString() ?? string.Empty;

        return Task.FromResult(new PaymentProviderEvent(Name, eventId, eventType, eventId, DateTimeOffset.UtcNow, data));
    }

    private async Task<PaymentResult> CreatePaymentAsync(PaymentRequest request, bool manualCapture, CancellationToken cancellationToken, string? recurringProcessingModel = null)
    {
        Dictionary<string, object?> body = new()
        {
            ["merchantAccount"] = options.MerchantAccount,
            ["reference"] = request.Metadata.GetValueOrDefault("orderId") ?? request.IdempotencyKey,
            ["amount"] = Amount(request.Amount),
            ["captureDelayHours"] = manualCapture ? "manual" : null,
            ["returnUrl"] = request.Metadata.GetValueOrDefault("returnUrl") ?? "https://localhost/payment/return",
            ["shopperReference"] = request.CustomerReference,
            ["recurringProcessingModel"] = recurringProcessingModel,
            ["metadata"] = request.Metadata
        };

        if (request.PaymentMethod is not null)
            body["storedPaymentMethodId"] = request.PaymentMethod.Reference;

        return await SendAsync(HttpMethod.Post, "payments", body, request.IdempotencyKey, cancellationToken);
    }

    private Task<PaymentResult> SendModificationAsync(string path, object body, CaptureRequest request, PaymentStatuses status, CancellationToken cancellationToken)
        => SendModificationAsync(path, body, request.PaymentId, request.Amount ?? new Money("USD", 0m), status, cancellationToken);

    private async Task<PaymentResult> SendModificationAsync(string path, object body, string paymentId, Money amount, PaymentStatuses status, CancellationToken cancellationToken)
    {
        PaymentResult result = await SendAsync(HttpMethod.Post, path, body, null, cancellationToken);
        if (!result.IsSuccessful)
            return result;

        Payment payment = new()
        {
            Id = paymentId,
            Provider = Name,
            ProviderReference = result.Payment!.ProviderReference,
            Amount = amount,
            Status = status
        };
        return new(payment);
    }

    private async Task<PaymentResult> SendAsync(HttpMethod method, string path, object body, string? idempotencyKey, CancellationToken cancellationToken)
    {
        options.Validate();
        using HttpRequestMessage message = new(method, path) { Content = JsonContent.Create(body) };
        message.Headers.TryAddWithoutValidation("X-API-Key", options.ApiKey);
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
            message.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);

        using HttpResponseMessage response = await httpClient.SendAsync(message, cancellationToken);
        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        using JsonDocument document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        if (!response.IsSuccessStatusCode)
            return PaymentResult.Failed(MapError(document.RootElement, (int)response.StatusCode));

        return new(MapPayment(document.RootElement));
    }

    private Payment MapPayment(JsonElement element)
    {
        string reference = element.TryGetProperty("pspReference", out JsonElement psp) ? psp.GetString()! : Guid.NewGuid().ToString("N");
        string resultCode = element.TryGetProperty("resultCode", out JsonElement result) ? result.GetString()! : "Received";
        Money amount = element.TryGetProperty("amount", out JsonElement amountElement) ? ParseAmount(amountElement) : new("USD", 0m);
        PaymentAction? action = null;
        if (element.TryGetProperty("action", out JsonElement actionElement))
        {
            Uri? url = actionElement.TryGetProperty("url", out JsonElement urlElement) && Uri.TryCreate(urlElement.GetString(), UriKind.Absolute, out Uri? parsed) ? parsed : null;
            Dictionary<string, string> data = actionElement.EnumerateObject().Where(property => property.Value.ValueKind == JsonValueKind.String).ToDictionary(property => property.Name, property => property.Value.GetString()!);
            action = url is null ? new(PaymentActionTypes.ProviderInstructions, Data: data) : new(PaymentActionTypes.Redirect, url, Data: data);
        }

        return new()
        {
            Id = reference,
            Provider = Name,
            ProviderReference = reference,
            Amount = amount,
            Status = resultCode switch
            {
                "Authorised" => PaymentStatuses.Authorized,
                "Received" or "Pending" or "PresentToShopper" => PaymentStatuses.Pending,
                "RedirectShopper" or "IdentifyShopper" or "ChallengeShopper" => PaymentStatuses.RequiresAction,
                "Cancelled" => PaymentStatuses.Cancelled,
                "Refused" or "Error" => PaymentStatuses.Failed,
                _ when action is not null => PaymentStatuses.RequiresAction,
                _ => PaymentStatuses.Pending
            },
            NextAction = action
        };
    }

    private static object Amount(Money amount) => new { currency = amount.Currency.ToUpperInvariant(), value = ToMinorUnits(amount) };

    private static Money ParseAmount(JsonElement amount)
    {
        string currency = amount.GetProperty("currency").GetString()!;
        long value = amount.GetProperty("value").GetInt64();
        return new(currency, value / 100m);
    }

    private static long ToMinorUnits(Money amount) => checked((long)decimal.Round(amount.Value * 100m, 0, MidpointRounding.AwayFromZero));

    private static PaymentError MapError(JsonElement root, int statusCode)
    {
        string code = root.TryGetProperty("errorCode", out JsonElement codeElement) ? codeElement.GetString() ?? statusCode.ToString(CultureInfo.InvariantCulture) : statusCode.ToString(CultureInfo.InvariantCulture);
        string message = root.TryGetProperty("message", out JsonElement messageElement) ? messageElement.GetString() ?? "Adyen request failed." : "Adyen request failed.";
        PaymentErrorTypes type = statusCode switch { 401 or 403 => PaymentErrorTypes.Authentication, 409 => PaymentErrorTypes.Duplicate, 422 => PaymentErrorTypes.Declined, 429 => PaymentErrorTypes.RateLimited, >= 500 => PaymentErrorTypes.ProviderUnavailable, _ => PaymentErrorTypes.InvalidRequest };
        return new(type, code, message, code, statusCode is 409 or 429 or >= 500);
    }

    private static string Escape(string? value) => (value ?? string.Empty).Replace("\\", "\\\\", StringComparison.Ordinal).Replace(":", "\\:", StringComparison.Ordinal);
}
