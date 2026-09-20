using AngryMonkey.CloudBooking;
using AngryMonkey.CloudCommerce;
using AngryMonkey.CloudCommerce.Components.Components;
using AngryMonkey.CloudCommerce.Demo;
using AngryMonkey.CloudLogistics;
using AngryMonkey.CloudPayments;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CloudCommerce.Demo.Tests;

public class WorkshopTests
{
    [Fact]
    public void Catalog_covers_every_public_component_and_parameter()
    {
        Type[] components = typeof(ProductCard).Assembly.GetExportedTypes().Where(type => type.Namespace == typeof(ProductCard).Namespace && typeof(IComponent).IsAssignableFrom(type)).ToArray();
        Assert.Equal(components.Select(type => type.Name).Order(), ComponentReferenceCatalog.All.Select(item => item.Tag).Order());
        foreach (ComponentReferenceDefinition definition in ComponentReferenceCatalog.All)
        {
            Type component = components.Single(type => type.Name == definition.Tag);
            string[] parameters = component.GetProperties().Where(property => Attribute.IsDefined(property, typeof(ParameterAttribute))).Select(property => property.Name).ToArray();
            Assert.Equal(parameters.Order(), definition.Parameters.Concat(definition.Events).Order());
        }
    }

    [Fact]
    public void Every_workshop_has_instructions_and_embedded_working_source()
    {
        string[] routes = ["/", "/storefront", "/booking", "/logistics", "/architecture", "/components", "/payments",
            .. ComponentReferenceCatalog.All.Select(item => $"/components/{item.Slug}"),
            .. PaymentProviderLabCatalog.All.Select(item => $"/payments/{item.Slug}")];
        foreach (string route in routes)
        {
            DemoGuide guide = DemoWorkshopCatalog.Find(route);
            Assert.NotEmpty(guide.Setup);
            Assert.True(guide.Steps.Count >= 3);
            Assert.NotEmpty(guide.Expected);
            Assert.NotEmpty(guide.Troubleshooting);
            Assert.All(guide.Files, file => Assert.NotEmpty(file.Content));
            Assert.Contains(guide.Files, file => file.Name.EndsWith(".razor"));
        }
    }

    [Fact]
    public async Task Demo_stores_and_payment_providers_are_isolated_between_sessions()
    {
        ServiceCollection services = new();
        services.AddCloudCommerce();
        services.AddCloudPayments();
        services.AddCloudLogistics();
        services.AddCloudBooking();
        services.AddDemoSessionStores();
        await using ServiceProvider container = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using IServiceScope first = container.CreateScope();
        using IServiceScope second = container.CreateScope();
        ICartStore firstCarts = first.ServiceProvider.GetRequiredService<ICartStore>();
        Cart cart = new() { Currency = "USD" };
        await firstCarts.SaveAsync(cart);
        Assert.Null(await second.ServiceProvider.GetRequiredService<ICartStore>().GetAsync(cart.Id));
        IPaymentProvider firstProvider = first.ServiceProvider.GetRequiredService<IPaymentProviderRegistry>().Get("Demo");
        PaymentResult payment = await firstProvider.CreateAsync(new() { Provider = "Demo", Amount = new("USD", 10), IdempotencyKey = "session-one" });
        Assert.Null(await second.ServiceProvider.GetRequiredService<IPaymentProviderRegistry>().Get("Demo").GetAsync(payment.Payment!.Id));
        Assert.NotSame(first.ServiceProvider.GetRequiredService<IBookingStore>(), second.ServiceProvider.GetRequiredService<IBookingStore>());
        Assert.NotSame(first.ServiceProvider.GetRequiredService<IInventoryStore>(), second.ServiceProvider.GetRequiredService<IInventoryStore>());
    }

    [Fact]
    public void Readiness_checks_do_not_allocate_clients_and_adapter_is_reused_until_keys_change()
    {
        CountingHttpClients clients = new();
        ProviderCredentialStore credentials = new();
        PaymentProviderLabDefinition definition = PaymentProviderLabCatalog.Find("stripe")!;
        credentials.Set("Stripe:SecretKey", "sk_test_workshop");
        using RuntimePaymentProviderFactory factory = new(clients, new Registry(), credentials);
        Assert.True(factory.IsReady(definition));
        Assert.True(factory.IsReady(definition));
        Assert.Equal(0, clients.Count);
        IPaymentProvider? first = factory.Resolve(definition);
        Assert.Same(first, factory.Resolve(definition));
        Assert.Equal(1, clients.Count);
        credentials.Set("Stripe:SecretKey", "sk_test_replacement");
        Assert.NotSame(first, factory.Resolve(definition));
        Assert.Equal(2, clients.Count);
        credentials.Clear(definition);
        Assert.False(factory.IsReady(definition));
        Assert.Null(factory.Resolve(definition));
    }

    [Theory]
    [InlineData("stripe", "Stripe:SecretKey", "sk_live_not_allowed")]
    [InlineData("stripe", "Stripe:SecretKey", "arbitrary-secret")]
    [InlineData("tap", "Tap:SecretKey", "sk_live_not_allowed")]
    [InlineData("paytabs", "PayTabs:ProfileId", "-1")]
    [InlineData("myfatoorah", "MyFatoorah:PaymentMethodId", "999999999999")]
    public void Invalid_credentials_cannot_construct_an_adapter(string slug, string key, string value)
    {
        ProviderCredentialStore credentials = new();
        credentials.Set(key, value);
        CountingHttpClients clients = new();
        using RuntimePaymentProviderFactory factory = new(clients, new Registry(), credentials);
        Assert.NotNull(factory.ValidationError(PaymentProviderLabCatalog.Find(slug)!));
        Assert.False(factory.IsReady(PaymentProviderLabCatalog.Find(slug)!));
        Assert.Equal(0, clients.Count);
    }

    [Theory]
    [InlineData(0, "USD")]
    [InlineData(-1, "USD")]
    [InlineData(10001, "USD")]
    [InlineData(10, "US")]
    [InlineData(10, "<X>")]
    public async Task Invalid_payment_inputs_are_rejected_before_dispatch(decimal amount, string currency)
    {
        CountingHttpClients clients = new();
        using RuntimePaymentProviderFactory factory = new(clients, new Registry(new DemoPaymentProvider()), new());
        SandboxPaymentRun run = await new SandboxPaymentRunner(factory).CreateAsync(PaymentProviderLabCatalog.Find("sandbox")!, amount, currency);
        Assert.False(run.IsSuccessful);
        Assert.NotNull(run.ExceptionMessage);
        Assert.Equal(0, clients.Count);
    }

    [Fact]
    public async Task Local_payment_lifecycle_supports_authorize_capture_refund_and_void()
    {
        using RuntimePaymentProviderFactory factory = new(new CountingHttpClients(), new Registry(new DemoPaymentProvider()), new());
        SandboxPaymentRunner runner = new(factory);
        PaymentProviderLabDefinition definition = PaymentProviderLabCatalog.Find("sandbox")!;
        SandboxPaymentRun authorized = await runner.CreateAsync(definition, 20, "USD", authorize: true);
        Assert.Equal(PaymentStatuses.Authorized, authorized.Result!.Payment!.Status);
        SandboxPaymentRun captured = await runner.ChangeLocalAsync(definition, authorized.Result.Payment, "capture");
        Assert.Equal(PaymentStatuses.Captured, captured.Result!.Payment!.Status);
        SandboxPaymentRun refunded = await runner.ChangeLocalAsync(definition, captured.Result.Payment, "refund");
        Assert.Equal(PaymentStatuses.Refunded, refunded.Result!.Payment!.Status);
        Assert.Equal(PaymentStatuses.Refunded, await runner.GetStatusAsync(definition, refunded.Result.Payment.Id));
        SandboxPaymentRun second = await runner.CreateAsync(definition, 10, "USD", authorize: true);
        SandboxPaymentRun voided = await runner.ChangeLocalAsync(definition, second.Result!.Payment!, "void");
        Assert.Equal(PaymentStatuses.Voided, voided.Result!.Payment!.Status);
    }

    [Fact]
    public async Task Provider_error_payloads_are_not_echoed_to_the_workshop()
    {
        ProviderCredentialStore credentials = new();
        credentials.Set("Stripe:SecretKey", "sk_test_fixture");
        using RuntimePaymentProviderFactory factory = new(new RejectingHttpClients(), new Registry(), credentials);
        SandboxPaymentRun run = await new SandboxPaymentRunner(factory).CreateAsync(PaymentProviderLabCatalog.Find("stripe")!, 10, "USD");
        Assert.False(run.IsSuccessful);
        Assert.DoesNotContain("sensitive-fixture-token", run.Result?.Error?.Message ?? run.ExceptionMessage ?? string.Empty);
        Assert.Equal("provider_error", run.Result?.Error?.Code);
    }

    private sealed class RejectingHttpClients : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new RejectingHandler());
    }

    private sealed class RejectingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("""{"error":{"type":"authentication_error","code":"api_key_invalid","message":"sensitive-fixture-token"}}""")
            });
    }

    private sealed class CountingHttpClients : IHttpClientFactory
    {
        public int Count { get; private set; }
        public HttpClient CreateClient(string name) { Count++; return new(); }
    }

    private sealed class Registry(params IPaymentProvider[] providers) : IPaymentProviderRegistry
    {
        public IReadOnlyCollection<IPaymentProvider> Providers { get; } = providers;
        public IPaymentProvider Get(string name) => Providers.Single(provider => provider.Name == name);
    }
}
