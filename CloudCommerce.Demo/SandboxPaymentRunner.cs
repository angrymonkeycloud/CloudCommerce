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
    public bool IsConfigured(PaymentProviderLabDefinition definition) => factory.IsReady(definition);
    public bool IsUsingPortalCredentials(PaymentProviderLabDefinition definition) => factory.IsUsingPortalCredentials(definition);
    public PaymentProviderCapabilities? GetCapabilities(PaymentProviderLabDefinition definition) => factory.Resolve(definition)?.Capabilities;

    public async Task<SandboxPaymentRun> CreateAsync(PaymentProviderLabDefinition definition, decimal amount, string currency, CancellationToken cancellationToken = default)
    {
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        Stopwatch stopwatch = Stopwatch.StartNew();

        IPaymentProvider? provider = factory.Resolve(definition);
        if (provider is null)
            return new(definition, startedAt, stopwatch.Elapsed, null, "Add the required keys above before running this lab.");

        try
        {
            PaymentRequest request = new()
            {
                Provider = definition.ProviderName,
                Amount = new Money(currency.Trim().ToUpperInvariant(), amount),
                IdempotencyKey = $"{definition.Slug}-lab-{Guid.NewGuid():N}",
                Description = $"CloudCommerce {definition.DisplayName} sandbox lab",
                // Stripe maps CustomerReference onto its `customer` parameter, which must be an
                // existing cus_… resource; a made-up value fails with resource_missing. Every other
                // adapter treats it as a free-form order reference, so only Stripe is left unset.
                CustomerReference = definition.ProviderName == "Stripe" ? null : $"sandbox-{Guid.NewGuid():N}",
                Metadata = SandboxCustomer.Metadata()
            };
            PaymentResult result = await provider.CreateAsync(request, cancellationToken);
            return new(definition, startedAt, stopwatch.Elapsed, result, null);
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException or TaskCanceledException or UriFormatException)
        {
            return new(definition, startedAt, stopwatch.Elapsed, null, exception.Message);
        }
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
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException or TaskCanceledException)
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