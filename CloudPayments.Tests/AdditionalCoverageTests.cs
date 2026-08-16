using AngryMonkey.Cloud.Geography;
using AngryMonkey.CloudPayments;

namespace CloudPayments.Tests;

public class AdditionalCoverageTests
{
    [Fact]
    public async Task CreateAsync_tax_disabled_never_calls_registered_tax_provider()
    {
        RecordingProvider provider = new();
        RecordingTaxProvider tax = new();
        PaymentService service = new(new PaymentProviderRegistry([provider]), new InMemoryPaymentStore(), new InMemoryIdempotencyStore(), new CloudPaymentsOptions(), tax);

        PaymentResult result = await service.CreateAsync(new() { Provider = provider.Name, Amount = new("USD", 19m), IdempotencyKey = "tax-off" });

        Assert.Equal(0, tax.Calls);
        Assert.Equal(19m, result.Payment!.Amount.Value);
    }

    [Fact]
    public async Task ProcessWebhookAsync_valid_signature_returns_normalized_event()
    {
        RecordingProvider provider = new();
        PaymentService service = Service(provider);

        PaymentProviderEvent result = await service.ProcessWebhookAsync(new(provider.Name, new Dictionary<string, string>(), "{}"u8.ToArray()));

        Assert.Equal(provider.Name, result.Provider);
        Assert.Equal("payment.captured", result.EventType);
        Assert.Equal(1, provider.ParseCalls);
    }

    [Fact]
    public async Task GetStatusAsync_saved_payment_does_not_call_provider()
    {
        RecordingProvider provider = new();
        InMemoryPaymentStore store = new();
        await store.SaveAsync(new() { Id = "stored", Provider = provider.Name, Amount = new("USD", 4m), Status = PaymentStatuses.Captured });
        PaymentService service = new(new PaymentProviderRegistry([provider]), store, new InMemoryIdempotencyStore(), new CloudPaymentsOptions());

        PaymentStatuses? result = await service.GetStatusAsync(provider.Name, "stored");

        Assert.Equal(PaymentStatuses.Captured, result);
        Assert.Equal(0, provider.StatusCalls);
    }

    [Fact]
    public void Registry_unknown_provider_reports_requested_name()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => new PaymentProviderRegistry([new RecordingProvider()]).Get("missing"));
        Assert.Contains("missing", exception.Message, StringComparison.Ordinal);
    }

    private static PaymentService Service(RecordingProvider provider) => new(new PaymentProviderRegistry([provider]), new InMemoryPaymentStore(), new InMemoryIdempotencyStore(), new CloudPaymentsOptions());

    private sealed class RecordingTaxProvider : ITaxProvider
    {
        public int Calls { get; private set; }
        public Task<TaxCalculationResult> CalculateAsync(TaxCalculationRequest request, CancellationToken cancellationToken = default) { Calls++; return Task.FromResult(new TaxCalculationResult(new Money(request.Amount.Currency, 2m))); }
    }

    private sealed class RecordingProvider : IPaymentProvider
    {
        public string Name => "additional";
        public PaymentProviderCapabilities Capabilities => PaymentProviderCapabilities.Create | PaymentProviderCapabilities.Webhooks;
        public int ParseCalls { get; private set; }
        public int StatusCalls { get; private set; }
        public Task<PaymentResult> CreateAsync(PaymentRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new PaymentResult(new Payment { Id = "created", Provider = Name, Amount = request.Amount, Status = PaymentStatuses.Captured }));
        public Task<PaymentResult> AuthorizeAsync(PaymentRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PaymentResult> CaptureAsync(CaptureRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PaymentResult> VoidAsync(VoidPaymentRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PaymentResult> RefundAsync(RefundRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PaymentResult> ChargeRecurringAsync(RecurringPaymentRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Payment?> GetAsync(string paymentId, CancellationToken cancellationToken = default) => Task.FromResult<Payment?>(null);
        public Task<PaymentStatuses?> GetStatusAsync(string paymentId, CancellationToken cancellationToken = default) { StatusCalls++; return Task.FromResult<PaymentStatuses?>(PaymentStatuses.Pending); }
        public Task<bool> ValidateWebhookAsync(PaymentWebhookRequest request, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<PaymentProviderEvent> ParseWebhookAsync(PaymentWebhookRequest request, CancellationToken cancellationToken = default) { ParseCalls++; return Task.FromResult(new PaymentProviderEvent(Name, "evt-1", "payment.captured", "created", DateTimeOffset.UtcNow, new Dictionary<string, string>())); }
    }
}