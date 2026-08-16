using AngryMonkey.Cloud.Geography;
using AngryMonkey.CloudCommerce;
using AngryMonkey.CloudPayments;

namespace CloudCommerce.Tests;

public class CheckoutCompensationTests
{
    [Fact]
    public async Task CheckoutAsync_payment_failure_releases_inventory_reservations()
    {
        RecordingLogistics logistics = new();
        CommerceService service = new(new InMemoryCartStore(), new InMemoryOrderStore(), [], payments: new FailingPayment(), logistics: logistics);
        Cart cart = await service.CreateCartAsync("USD");
        await service.AddItemAsync(cart.Id, new("item", "Item", new Money("USD", 20m)));
        CheckoutRequest request = new() { CartId = cart.Id, IdempotencyKey = "checkout-1", PaymentProvider = "fake", Shipping = new("carrier", "standard", new Money("USD", 5m), Guid.NewGuid()) };

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CheckoutAsync(request));

        Assert.Equal(logistics.Reserved, logistics.Released);
    }

    private sealed class FailingPayment : ICommercePaymentGateway
    {
        public Task<Payment> PayAsync(Order order, CheckoutRequest request, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Declined");
        public Task CancelAsync(Order order, string paymentReference, string idempotencyKey, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingLogistics : ICommerceLogisticsGateway
    {
        public IReadOnlyList<Guid> Reserved { get; private set; } = [];
        public IReadOnlyList<Guid> Released { get; private set; } = [];
        public Task<IReadOnlyList<Guid>> ReserveAsync(Cart cart, ShippingSelection shipping, CancellationToken cancellationToken = default) { Reserved = [Guid.NewGuid()]; return Task.FromResult(Reserved); }
        public Task ReleaseAsync(IReadOnlyList<Guid> reservationIds, CancellationToken cancellationToken = default) { Released = reservationIds; return Task.CompletedTask; }
    }
}
