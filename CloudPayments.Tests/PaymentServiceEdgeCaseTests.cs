using AngryMonkey.Cloud.Geography;
using AngryMonkey.CloudPayments;

namespace CloudPayments.Tests;

public class PaymentServiceEdgeCaseTests
{
    [Fact]
    public async Task CreateAsync_tax_enabled_adds_tax_before_provider_call()
    {
        RecordingProvider provider = new();
        PaymentService service = new(new PaymentProviderRegistry([provider]), new InMemoryPaymentStore(), new InMemoryIdempotencyStore(), new CloudPaymentsOptions { TaxCalculationEnabled = true }, new FixedTaxProvider());

        PaymentResult result = await service.CreateAsync(new() { Provider = provider.Name, Amount = new("USD", 10m), IdempotencyKey = "tax-1" });

        Assert.Equal(12m, provider.LastRequest!.Amount.Value);
        Assert.Equal(12m, result.Payment!.Amount.Value);
    }

    [Fact]
    public async Task CreateAsync_tax_enabled_without_provider_throws()
    {
        RecordingProvider provider = new();
        PaymentService service = new(new PaymentProviderRegistry([provider]), new InMemoryPaymentStore(), new InMemoryIdempotencyStore(), new CloudPaymentsOptions { TaxCalculationEnabled = true });

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(new() { Provider = provider.Name, Amount = new("USD", 10m), IdempotencyKey = "tax-2" }));
    }

    [Fact]
    public async Task AuthorizeAsync_missing_capability_throws_before_call()
    {
        RecordingProvider provider = new(PaymentProviderCapabilities.Create);
        PaymentService service = Service(provider);

        await Assert.ThrowsAsync<NotSupportedException>(() => service.AuthorizeAsync(new() { Provider = provider.Name, Amount = new("USD", 10m), IdempotencyKey = "auth-1" }));

        Assert.Equal(0, provider.AuthorizeCalls);
    }

    [Fact]
    public async Task ProcessWebhookAsync_invalid_signature_throws()
    {
        RecordingProvider provider = new(PaymentProviderCapabilities.Webhooks) { WebhookValid = false };
        PaymentService service = Service(provider);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ProcessWebhookAsync(new(provider.Name, new Dictionary<string, string>(), ReadOnlyMemory<byte>.Empty)));
    }

    [Fact]
    public void Registry_provider_lookup_is_case_insensitive()
    {
        RecordingProvider provider = new();

        IPaymentProvider resolved = new PaymentProviderRegistry([provider]).Get("RECORDING");

        Assert.Same(provider, resolved);
    }

    [Fact]
    public async Task GetAsync_store_miss_falls_back_to_provider()
    {
        RecordingProvider provider = new();
        provider.PaymentToReturn = new() { Id = "provider-payment", Provider = provider.Name, Amount = new("USD", 1m), Status = PaymentStatuses.Pending };

        Payment? payment = await Service(provider).GetAsync(provider.Name, "provider-payment");

        Assert.Equal("provider-payment", payment!.Id);
    }

    /// <summary>
    /// The hosted-form case: the payment is stored the moment it is created, while it is still
    /// waiting on the customer, and succeeds later at the provider where nothing tells the store.
    /// Answering that from the store would report it unpaid for good.
    /// </summary>
    [Fact]
    public async Task GetAsync_rereads_a_payment_that_was_still_in_flight_when_it_was_stored()
    {
        RecordingProvider provider = new();
        InMemoryPaymentStore store = new();
        await store.SaveAsync(new() { Id = "pi_1", Provider = provider.Name, Amount = new("USD", 25m), Status = PaymentStatuses.RequiresAction });
        provider.PaymentToReturn = new() { Id = "pi_1", Provider = provider.Name, Amount = new("USD", 25m), Status = PaymentStatuses.Captured, Metadata = { ["linkId"] = "abc" } };
        PaymentService service = new(new PaymentProviderRegistry([provider]), store, new InMemoryIdempotencyStore(), new CloudPaymentsOptions());

        Payment? payment = await service.GetAsync(provider.Name, "pi_1");

        Assert.Equal(PaymentStatuses.Captured, payment!.Status);
        Assert.Equal("abc", payment.Metadata["linkId"]);
        Assert.Equal(PaymentStatuses.Captured, (await store.GetAsync(provider.Name, "pi_1"))!.Status);
    }

    [Fact]
    public async Task CreateAsync_same_key_is_isolated_by_provider()
    {
        RecordingProvider first = new(name: "first");
        RecordingProvider second = new(name: "second");
        PaymentService service = new(new PaymentProviderRegistry([first, second]), new InMemoryPaymentStore(), new InMemoryIdempotencyStore(), new CloudPaymentsOptions());
        PaymentRequest firstRequest = new() { Provider = first.Name, Amount = new("USD", 1m), IdempotencyKey = "shared" };
        PaymentRequest secondRequest = new() { Provider = second.Name, Amount = new("USD", 1m), IdempotencyKey = "shared" };

        await service.CreateAsync(firstRequest);
        await service.CreateAsync(secondRequest);

        Assert.Equal(1, first.CreateCalls);
        Assert.Equal(1, second.CreateCalls);
    }

    [Fact]
    public async Task CreateAsync_tax_currency_mismatch_throws()
    {
        RecordingProvider provider = new();
        PaymentService service = new(new PaymentProviderRegistry([provider]), new InMemoryPaymentStore(), new InMemoryIdempotencyStore(), new CloudPaymentsOptions { TaxCalculationEnabled = true }, new MismatchedTaxProvider());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(new() { Provider = provider.Name, Amount = new("USD", 10m), IdempotencyKey = "tax-currency" }));
    }

    private static PaymentService Service(RecordingProvider provider) => new(new PaymentProviderRegistry([provider]), new InMemoryPaymentStore(), new InMemoryIdempotencyStore(), new CloudPaymentsOptions());

    private sealed class MismatchedTaxProvider : ITaxProvider
    {
        public Task<TaxCalculationResult> CalculateAsync(TaxCalculationRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new TaxCalculationResult(new Money("EUR", 2m)));
    }

    private sealed class FixedTaxProvider : ITaxProvider
    {
        public Task<TaxCalculationResult> CalculateAsync(TaxCalculationRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new TaxCalculationResult(new Money(request.Amount.Currency, 2m)));
    }

    private sealed class RecordingProvider(PaymentProviderCapabilities? capabilities = null, string name = "recording") : IPaymentProvider
    {
        public string Name => name;
        public PaymentProviderCapabilities Capabilities { get; } = capabilities ?? Enum.GetValues<PaymentProviderCapabilities>().Aggregate((left, right) => left | right);
        public PaymentRequest? LastRequest { get; private set; }
        public int AuthorizeCalls { get; private set; }
        public int CreateCalls { get; private set; }
        public bool WebhookValid { get; init; } = true;
        public Payment? PaymentToReturn { get; set; }

        public Task<PaymentResult> CreateAsync(PaymentRequest request, CancellationToken cancellationToken = default) { CreateCalls++; LastRequest = request; return Task.FromResult(Result(request.Amount, PaymentStatuses.Captured)); }
        public Task<PaymentResult> AuthorizeAsync(PaymentRequest request, CancellationToken cancellationToken = default) { AuthorizeCalls++; return Task.FromResult(Result(request.Amount, PaymentStatuses.Authorized)); }
        public Task<PaymentResult> CaptureAsync(CaptureRequest request, CancellationToken cancellationToken = default) => Task.FromResult(Result(request.Amount ?? new Money("USD", 0m), PaymentStatuses.Captured));
        public Task<PaymentResult> VoidAsync(VoidPaymentRequest request, CancellationToken cancellationToken = default) => Task.FromResult(Result(new Money("USD", 0m), PaymentStatuses.Voided));
        public Task<PaymentResult> RefundAsync(RefundRequest request, CancellationToken cancellationToken = default) => Task.FromResult(Result(request.Amount, PaymentStatuses.Refunded));
        public Task<PaymentResult> ChargeRecurringAsync(RecurringPaymentRequest request, CancellationToken cancellationToken = default) => Task.FromResult(Result(request.Amount, PaymentStatuses.Captured));
        public Task<Payment?> GetAsync(string paymentId, CancellationToken cancellationToken = default) => Task.FromResult(PaymentToReturn);
        public Task<PaymentStatuses?> GetStatusAsync(string paymentId, CancellationToken cancellationToken = default) => Task.FromResult(PaymentToReturn?.Status);
        public Task<bool> ValidateWebhookAsync(PaymentWebhookRequest request, CancellationToken cancellationToken = default) => Task.FromResult(WebhookValid);
        public Task<PaymentProviderEvent> ParseWebhookAsync(PaymentWebhookRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new PaymentProviderEvent(Name, "event", "payment", null, DateTimeOffset.UtcNow, new Dictionary<string, string>()));
        private PaymentResult Result(Money amount, PaymentStatuses status) => new(new Payment { Id = "payment", Provider = Name, Amount = amount, Status = status });
    }
}
