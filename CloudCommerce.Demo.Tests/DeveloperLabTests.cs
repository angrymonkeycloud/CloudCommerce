using Xunit;
using AngryMonkey.Cloud.Geography;
using AngryMonkey.CloudCommerce.Demo;
using AngryMonkey.CloudPayments;

namespace CloudCommerce.Demo.Tests;

public class DeveloperLabCatalogTests
{
    [Fact]
    public void Payment_labs_have_unique_routes_real_packages_and_secure_setup()
    {
        IReadOnlyList<PaymentProviderLabDefinition> realProviders = [.. PaymentProviderLabCatalog.All.Where(definition => definition.RequiresCredentials)];
        Assert.Equal(7, realProviders.Count);
        Assert.Equal(7, realProviders.Select(item => item.Slug).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(7, realProviders.Select(item => item.ProviderName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Single(PaymentProviderLabCatalog.All, definition => definition.Slug == "sandbox");
        Assert.All(realProviders, definition =>
        {
            Assert.StartsWith("AngryMonkey.CloudPayments.", definition.PackageName);
            Assert.Equal("https", definition.TestingUrl.Scheme);
            Assert.NotEmpty(definition.ConfigurationKeys);
            Assert.Contains(definition.ProviderName, definition.RequestCode, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("YOUR_TEST_VALUE", definition.RegistrationCode, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Component_catalog_identifies_every_item_as_a_real_package_component()
    {
        Assert.Equal(12, ComponentReferenceCatalog.All.Count);
        Assert.Equal(12, ComponentReferenceCatalog.All.Select(item => item.Slug).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(ComponentReferenceCatalog.All, definition =>
        {
            Assert.NotEmpty(definition.Tag);
            Assert.Contains($"/{definition.Tag}.razor", ComponentReferenceCatalog.SourceUrl(definition), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Configuration_commands_use_user_secrets_and_never_embed_credentials()
    {
        PaymentProviderLabDefinition definition = PaymentProviderLabCatalog.Find("myfatoorah")!;
        string commands = PaymentProviderLabCatalog.ConfigurationCommands(definition);

        Assert.Contains("dotnet user-secrets set", commands, StringComparison.Ordinal);
        Assert.Contains("MyFatoorah:ApiToken", commands, StringComparison.Ordinal);
        Assert.Contains("YOUR_TEST_VALUE", commands, StringComparison.Ordinal);
        Assert.DoesNotContain("ApiToken =", commands, StringComparison.Ordinal);
    }
}

public class SandboxPaymentRunnerTests
{
    [Fact]
    public async Task CreateAsync_dispatches_to_real_provider_name_with_safe_test_metadata()
    {
        StubProvider provider = new("SkipCash");
        StubRegistry registry = new(provider);
        RuntimePaymentProviderFactory factory = new(new StubHttpClientFactory(), registry, new ProviderCredentialStore());
        SandboxPaymentRunner runner = new(factory);
        
        PaymentProviderLabDefinition definition = PaymentProviderLabCatalog.Find("skipcash")!;

        SandboxPaymentRun run = await runner.CreateAsync(definition, 10m, "qar");

        Assert.True(run.IsSuccessful);
        Assert.NotNull(provider.Request);
        Assert.Equal("SkipCash", provider.Request.Provider);
        Assert.Equal("QAR", provider.Request.Amount.Currency);
        Assert.Equal("sandbox@example.invalid", provider.Request.Metadata["email"]);
        Assert.StartsWith("skipcash-lab-", provider.Request.IdempotencyKey, StringComparison.Ordinal);
    }

    [Fact]
    public void IsConfigured_uses_registered_adapter_name_not_demo_card_name()
    {
        StubRegistry registry = new(new StubProvider("Tap"));
        RuntimePaymentProviderFactory factory = new(new StubHttpClientFactory(), registry, new ProviderCredentialStore());
        SandboxPaymentRunner runner = new(factory);

        Assert.True(runner.IsConfigured(PaymentProviderLabCatalog.Find("tap")!));
        Assert.False(runner.IsConfigured(PaymentProviderLabCatalog.Find("stripe")!));
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }


    private sealed class StubRegistry(params IPaymentProvider[] providers) : IPaymentProviderRegistry
    {
        public IReadOnlyCollection<IPaymentProvider> Providers { get; } = providers;
        public IPaymentProvider Get(string name) => Providers.Single(provider => provider.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class StubProvider(string name) : IPaymentProvider
    {
        public PaymentRequest Request { get; private set; } = null!;
        public string Name { get; } = name;
        public PaymentProviderCapabilities Capabilities => PaymentProviderCapabilities.Create;
        public Task<PaymentResult> CreateAsync(PaymentRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            Payment payment = new() { Id = "sandbox-payment", Provider = request.Provider, Amount = request.Amount, Status = PaymentStatuses.RequiresAction };
            return Task.FromResult(new PaymentResult(payment));
        }
        public Task<PaymentResult> AuthorizeAsync(PaymentRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PaymentResult> CaptureAsync(CaptureRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PaymentResult> VoidAsync(VoidPaymentRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PaymentResult> RefundAsync(RefundRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PaymentResult> ChargeRecurringAsync(RecurringPaymentRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Payment?> GetAsync(string paymentId, CancellationToken cancellationToken = default) => Task.FromResult<Payment?>(null);
        public Task<PaymentStatuses?> GetStatusAsync(string paymentId, CancellationToken cancellationToken = default) => Task.FromResult<PaymentStatuses?>(null);
        public Task<bool> ValidateWebhookAsync(PaymentWebhookRequest request, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<PaymentProviderEvent> ParseWebhookAsync(PaymentWebhookRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}