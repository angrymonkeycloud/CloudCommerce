using AngryMonkey.Cloud.Geography;
using AngryMonkey.CloudCommerce;
using AngryMonkey.CloudPayments;

namespace CloudCommerce.Tests;

public class PaymentCompensationTests
{
    [Fact]
    public async Task CheckoutAsync_post_payment_failure_cancels_payment()
    {
        RecordingPayment payment = new();
        CommerceService service = new(new InMemoryCartStore(), new InMemoryOrderStore(), [], payments: payment, bookings: new FailingBooking());
        Cart cart = await service.CreateCartAsync("USD");
        await service.AddItemAsync(cart.Id, new("service", "Service", new Money("USD", 50m)));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CheckoutAsync(new() { CartId = cart.Id, IdempotencyKey = "checkout-2", PaymentProvider = "fake", BookingReservationIds = [Guid.NewGuid()] }));

        Assert.True(payment.WasCancelled);
        Assert.Equal("checkout-2:compensate", payment.CancellationKey);
    }

    private sealed class RecordingPayment : ICommercePaymentGateway
    {
        public bool WasCancelled { get; private set; }
        public string? CancellationKey { get; private set; }
        public Task<Payment> PayAsync(Order order, CheckoutRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new Payment { Id = "payment-1", Provider = "fake", Amount = order.Totals.Final, Status = PaymentStatuses.Captured });
        public Task CancelAsync(Order order, string paymentReference, string idempotencyKey, CancellationToken cancellationToken = default) { WasCancelled = true; CancellationKey = idempotencyKey; return Task.CompletedTask; }
    }

    private sealed class FailingBooking : ICommerceBookingGateway
    {
        public Task ConfirmAsync(IReadOnlyList<Guid> reservationIds, string? paymentReference, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Reservation failed");
        public Task CancelAsync(IReadOnlyList<Guid> reservationIds, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
