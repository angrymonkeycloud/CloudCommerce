using System.Diagnostics;
using AngryMonkey.Cloud.Geography;
using AngryMonkey.CloudPayments;

namespace AngryMonkey.CloudCommerce.Demo;

public sealed record SandboxPaymentRun(PaymentProviderLabDefinition Provider, DateTimeOffset StartedAt, TimeSpan Duration, PaymentResult? Result, string? ExceptionMessage)
{
    public bool IsSuccessful => Result?.IsSuccessful == true;
}

public sealed class SandboxPaymentRunner(RuntimePaymentProviderFactory factory)
{
    public void ResetConnections() => factory.Dispose();
    public string? ValidationError(PaymentProviderLabDefinition definition) => factory.ValidationError(definition);
    public bool IsConfigured(PaymentProviderLabDefinition definition) => factory.IsReady(definition);
    public bool IsUsingPortalCredentials(PaymentProviderLabDefinition definition) => factory.IsUsingPortalCredentials(definition);
    public PaymentProviderCapabilities? GetCapabilities(PaymentProviderLabDefinition definition) => factory.Resolve(definition)?.Capabilities;

    public async Task<SandboxPaymentRun> CreateAsync(PaymentProviderLabDefinition definition, decimal amount, string currency, CancellationToken cancellationToken = default, bool authorize = false)
    {
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        Stopwatch stopwatch = Stopwatch.StartNew();

        string normalizedCurrency = currency?.Trim().ToUpperInvariant() ?? string.Empty;
        if (amount < 0.01m || amount > 10000 || normalizedCurrency.Length != 3 || !normalizedCurrency.All(character => character is >= 'A' and <= 'Z') || (definition.ProviderName == "SkipCash" && normalizedCurrency != "QAR"))
            return new(definition, startedAt, stopwatch.Elapsed, null, "Use an amount between 0.01 and 10,000 and a three-letter currency. SkipCash requires QAR.");
        if (factory.ValidationError(definition) is { } validation)
            return new(definition, startedAt, stopwatch.Elapsed, null, validation);
        IPaymentProvider? provider = factory.Resolve(definition);
        if (provider is null)
            return new(definition, startedAt, stopwatch.Elapsed, null, "Add the required keys above before running this lab.");

        try
        {
            PaymentRequest request = new()
            {
                Provider = definition.ProviderName,
                Amount = new Money(normalizedCurrency, amount),
                IdempotencyKey = $"{definition.Slug}-lab-{Guid.NewGuid():N}",
                Description = $"CloudCommerce {definition.DisplayName} sandbox lab",
                // Stripe maps CustomerReference onto its `customer` parameter, which must be an
                // existing cus_… resource; a made-up value fails with resource_missing. Every other
                // adapter treats it as a free-form order reference, so only Stripe is left unset.
                CustomerReference = definition.ProviderName == "Stripe" ? null : $"sandbox-{Guid.NewGuid():N}",
                Metadata = SandboxCustomer.Metadata()
            };
            PaymentResult result = authorize && !definition.RequiresCredentials ? await provider.AuthorizeAsync(request, cancellationToken) : await provider.CreateAsync(request, cancellationToken);
            result = SafeResult(result);
            return new(definition, startedAt, stopwatch.Elapsed, result, null);
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException or TaskCanceledException or FormatException or System.Text.Json.JsonException)
        {
            return new(definition, startedAt, stopwatch.Elapsed, null, "The provider request failed. Verify test credentials, account activation and currency, then retry.");
        }
    }

    private static PaymentResult SafeResult(PaymentResult result) => result.Error is { } error
        ? PaymentResult.Failed(new(error.Type, "provider_error", "The provider rejected the request. Check test credentials, currency and account setup in Instructions.", IsRetryable: error.IsRetryable))
        : result;

    public async Task<SandboxPaymentRun> ChangeLocalAsync(PaymentProviderLabDefinition definition, Payment payment, string operation)
    {
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        if (definition.RequiresCredentials || factory.Resolve(definition) is not DemoPaymentProvider provider)
            return new(definition, startedAt, TimeSpan.Zero, null, "Lifecycle actions here are available only in the built-in sandbox.");
        PaymentResult result = operation switch
        {
            "capture" when payment.Status == PaymentStatuses.Authorized => await provider.CaptureAsync(new(provider.Name, payment.Id)),
            "void" when payment.Status == PaymentStatuses.Authorized => await provider.VoidAsync(new(provider.Name, payment.Id)),
            "refund" when payment.Status == PaymentStatuses.Captured => await provider.RefundAsync(new(provider.Name, payment.Id, payment.Amount, $"refund-{payment.Id}")),
            _ => PaymentResult.Failed(new(PaymentErrorTypes.InvalidRequest, "invalid_state", "This action is unavailable for the payment's current state."))
        };
        return new(definition, startedAt, DateTimeOffset.UtcNow - startedAt, result, null);
    }

    public async Task<PaymentStatuses?> GetStatusAsync(PaymentProviderLabDefinition definition, string paymentId, CancellationToken cancellationToken = default)
    {
        IPaymentProvider? provider = factory.Resolve(definition);
        if (provider is null)
            return null;

        try
        {
            return await provider.GetStatusAsync(paymentId, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException or TaskCanceledException or FormatException or System.Text.Json.JsonException)
        {
            return null;
        }
    }
}

public static class SandboxCustomer
{
    public static IReadOnlyDictionary<string, string> Qatar { get; } = new Dictionary<string, string>
    {
        ["first_name"] = "Sandbox", ["last_name"] = "Customer", ["phone"] = "+97450000000", ["email"] = "sandbox@example.invalid", ["street"] = "Test Street 1", ["city"] = "Doha", ["state"] = "Doha", ["country"] = "QA", ["postal_code"] = "00000"
    };

    public static Dictionary<string, string> Metadata() => new(Qatar)
    {
        ["customer_name"] = "Sandbox Customer", ["customer_email"] = "sandbox@example.invalid", ["customer_mobile"] = "+97450000000", ["customer_phone"] = "+97450000000", ["phone_country_code"] = "974", ["phone_number"] = "50000000", ["orderId"] = $"lab-{Guid.NewGuid():N}", ["returnUrl"] = "https://localhost/payment/return"
    };
}
