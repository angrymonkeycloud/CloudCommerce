using AngryMonkey.Cloud.Geography;
using AngryMonkey.CloudPayments;

namespace CloudPayments.Tests;

public class PaymentServiceTests
{
    [Fact]
    public async Task CreateAsync_repeated_idempotency_key_calls_provider_once()
    {
        FakeProvider provider = new();
        PaymentService service = new(new PaymentProviderRegistry([provider]), new InMemoryPaymentStore(), new InMemoryIdempotencyStore(), new CloudPaymentsOptions());
        PaymentRequest request = new() { Provider = provider.Name, Amount = new("USD", 12.50m), IdempotencyKey = "order-1" };

        PaymentResult first = await service.CreateAsync(request);
        PaymentResult second = await service.CreateAsync(request);

        Assert.True(first.IsSuccessful);
        Assert.Equal(first.Payment!.Id, second.Payment!.Id);
        Assert.Equal(1, provider.CreateCalls);
    }

    private sealed class FakeProvider : IPaymentProvider
    {
        public string Name => "fake";
        public PaymentProviderCapabilities Capabilities => PaymentProviderCapabilities.Create | PaymentProviderCapabilities.Webhooks;
        public int CreateCalls { get; private set; }
        public Task<PaymentResult> CreateAsync(PaymentRequest request, CancellationToken cancellationToken = default) { CreateCalls++; return Task.FromResult(new PaymentResult(new Payment { Id = Guid.NewGuid().ToString(), Provider = Name, Amount = request.Amount, Status = PaymentStatuses.Captured })); }
        public Task<PaymentResult> AuthorizeAsync(PaymentRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PaymentResult> CaptureAsync(CaptureRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PaymentResult> VoidAsync(VoidPaymentRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PaymentResult> RefundAsync(RefundRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PaymentResult> ChargeRecurringAsync(RecurringPaymentRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Payment?> GetAsync(string paymentId, CancellationToken cancellationToken = default) => Task.FromResult<Payment?>(null);
        public Task<PaymentStatuses?> GetStatusAsync(string paymentId, CancellationToken cancellationToken = default) => Task.FromResult<PaymentStatuses?>(null);
        public Task<bool> ValidateWebhookAsync(PaymentWebhookRequest request, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<PaymentProviderEvent> ParseWebhookAsync(PaymentWebhookRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new PaymentProviderEvent(Name, "event", "payment", null, DateTimeOffset.UtcNow, new Dictionary<string, string>()));
    }
}
