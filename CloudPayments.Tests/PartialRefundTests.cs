using AngryMonkey.Cloud.Geography;
using AngryMonkey.CloudPayments;

namespace CloudPayments.Tests;

public class PartialRefundTests
{
    [Fact]
    public async Task RefundAsync_partial_request_uses_partial_refund_capability()
    {
        PartialRefundProvider provider = new();
        PaymentService service = new(new PaymentProviderRegistry([provider]), new InMemoryPaymentStore(), new InMemoryIdempotencyStore(), new CloudPaymentsOptions());

        PaymentResult result = await service.RefundAsync(new(provider.Name, "payment-1", new Money("USD", 5m), "refund-1", IsPartial: true));

        Assert.True(result.IsSuccessful);
        Assert.Equal(PaymentStatuses.PartiallyRefunded, result.Payment!.Status);
    }

    private sealed class PartialRefundProvider : IPaymentProvider
    {
        public string Name => "partial";
        public PaymentProviderCapabilities Capabilities => PaymentProviderCapabilities.PartialRefund;
        public Task<PaymentResult> RefundAsync(RefundRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new PaymentResult(new Payment { Id = request.PaymentId, Provider = Name, Amount = request.Amount, Status = PaymentStatuses.PartiallyRefunded }));
        public Task<PaymentResult> CreateAsync(PaymentRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PaymentResult> AuthorizeAsync(PaymentRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PaymentResult> CaptureAsync(CaptureRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PaymentResult> VoidAsync(VoidPaymentRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PaymentResult> ChargeRecurringAsync(RecurringPaymentRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Payment?> GetAsync(string paymentId, CancellationToken cancellationToken = default) => Task.FromResult<Payment?>(null);
        public Task<PaymentStatuses?> GetStatusAsync(string paymentId, CancellationToken cancellationToken = default) => Task.FromResult<PaymentStatuses?>(null);
        public Task<bool> ValidateWebhookAsync(PaymentWebhookRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PaymentProviderEvent> ParseWebhookAsync(PaymentWebhookRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
