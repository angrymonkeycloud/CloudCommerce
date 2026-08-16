using AngryMonkey.Cloud.Geography;
using AngryMonkey.CloudCommerce;
using AngryMonkey.CloudPayments;

namespace CloudCommerce.Tests;

public class CommerceEdgeCaseTests
{
    [Fact]
    public async Task AddItemAsync_same_product_merges_quantity()
    {
        CommerceService service = Service();
        Cart cart = await service.CreateCartAsync("usd");
        ProductPresentation product = new("one", "One", new Money("USD", 5m), Reference: "sku");
        await service.AddItemAsync(cart.Id, product, 2);

        Cart updated = await service.AddItemAsync(cart.Id, product, 3);

        Assert.Equal(5, Assert.Single(updated.Items).Quantity);
        Assert.Equal("USD", updated.Currency);
    }

    [Fact]
    public async Task AddItemAsync_currency_mismatch_throws()
    {
        CommerceService service = Service();
        Cart cart = await service.CreateCartAsync("USD");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddItemAsync(cart.Id, new("one", "One", new Money("EUR", 5m))));
    }

    [Fact]
    public async Task ApplyCouponAsync_duplicate_is_case_insensitive()
    {
        CommerceService service = Service();
        Cart cart = await service.CreateCartAsync("USD");
        await service.ApplyCouponAsync(cart.Id, "SAVE");

        Cart updated = await service.ApplyCouponAsync(cart.Id, "save");

        Assert.Single(updated.CouponCodes);
    }

    [Fact]
    public async Task CalculateTotalsAsync_caps_discounts_at_items_total()
    {
        CommerceService service = new(new InMemoryCartStore(), new InMemoryOrderStore(), [new ExcessiveDiscount()]);
        Cart cart = await service.CreateCartAsync("USD");
        await service.AddItemAsync(cart.Id, new("one", "One", new Money("USD", 10m)));

        CommerceTotals totals = await service.CalculateTotalsAsync(cart.Id);

        Assert.Equal(10m, totals.Discount.Value);
        Assert.Equal(0m, totals.Final.Value);
    }

    [Fact]
    public async Task CheckoutAsync_empty_cart_throws()
    {
        CommerceService service = Service();
        Cart cart = await service.CreateCartAsync("USD");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CheckoutAsync(new() { CartId = cart.Id, IdempotencyKey = "empty" }));
    }

    [Fact]
    public async Task CheckoutAsync_requires_action_stays_awaiting_payment_and_does_not_confirm_booking()
    {
        RecordingBooking booking = new();
        CommerceService service = new(new InMemoryCartStore(), new InMemoryOrderStore(), [], payments: new ActionPayment(), bookings: booking);
        Cart cart = await service.CreateCartAsync("USD");
        await service.AddItemAsync(cart.Id, new("one", "One", new Money("USD", 10m)));
        Guid reservation = Guid.NewGuid();

        Order order = await service.CheckoutAsync(new() { CartId = cart.Id, IdempotencyKey = "action", PaymentProvider = "provider", BookingReservationIds = [reservation] });

        Assert.Equal(OrderStatuses.AwaitingPayment, order.Status);
        Assert.Equal(PaymentStatuses.RequiresAction, order.PaymentStatus);
        Assert.NotNull(order.PaymentAction);
        Assert.Empty(booking.Confirmed);
    }

    [Fact]
    public async Task InMemoryOrderStore_filters_by_account()
    {
        InMemoryOrderStore store = new();
        CommerceAccountReference account = new(UserId: Guid.NewGuid());
        Order matching = Order(account);
        await store.SaveAsync(matching);
        await store.SaveAsync(Order(new(UserId: Guid.NewGuid())));

        IReadOnlyList<Order> orders = await store.GetForAccountAsync(account);

        Assert.Equal(matching.Id, Assert.Single(orders).Id);
    }

    [Fact]
    public async Task CheckoutAsync_repeated_idempotency_key_returns_same_order_without_second_payment()
    {
        CountingPayment payment = new();
        InMemoryCheckoutIdempotencyStore idempotency = new();
        CommerceService service = new(new InMemoryCartStore(), new InMemoryOrderStore(), [], payments: payment, checkoutIdempotency: idempotency);
        Cart cart = await service.CreateCartAsync("USD");
        await service.AddItemAsync(cart.Id, new("one", "One", new Money("USD", 10m)));
        CheckoutRequest request = new() { CartId = cart.Id, IdempotencyKey = "checkout-repeat", PaymentProvider = "provider" };

        Order first = await service.CheckoutAsync(request);
        Order second = await service.CheckoutAsync(request);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, payment.Calls);
    }

    private static CommerceService Service() => new(new InMemoryCartStore(), new InMemoryOrderStore(), []);

    private static Order Order(CommerceAccountReference account) => new() { CartId = Guid.NewGuid(), Account = account, Items = [], Totals = new() { Items = new("USD", 0m), Discount = new("USD", 0m), Shipping = new("USD", 0m), Tax = new("USD", 0m), Final = new("USD", 0m) }, Status = OrderStatuses.Pending };

    private sealed class ExcessiveDiscount : IDiscountProvider
    {
        public Task<IReadOnlyList<DiscountAdjustment>> CalculateAsync(Cart cart, Money itemsTotal, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DiscountAdjustment>>([new("ALL", "Everything", new Money(cart.Currency, 20m))]);
    }

    private sealed class CountingPayment : ICommercePaymentGateway
    {
        public int Calls { get; private set; }
        public Task<Payment> PayAsync(Order order, CheckoutRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new Payment { Id = "payment-counted", Provider = "provider", Amount = order.Totals.Final, Status = PaymentStatuses.Captured });
        }

        public Task CancelAsync(Order order, string paymentReference, string idempotencyKey, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class ActionPayment : ICommercePaymentGateway
    {
        public Task<Payment> PayAsync(Order order, CheckoutRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new Payment { Id = "payment-1", Provider = "provider", Amount = order.Totals.Final, Status = PaymentStatuses.RequiresAction, NextAction = new(PaymentActionTypes.Redirect, new Uri("https://provider.test/action")) });
        public Task CancelAsync(Order order, string paymentReference, string idempotencyKey, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingBooking : ICommerceBookingGateway
    {
        public List<Guid> Confirmed { get; } = [];
        public Task ConfirmAsync(IReadOnlyList<Guid> reservationIds, string? paymentReference, CancellationToken cancellationToken = default) { Confirmed.AddRange(reservationIds); return Task.CompletedTask; }
        public Task CancelAsync(IReadOnlyList<Guid> reservationIds, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
