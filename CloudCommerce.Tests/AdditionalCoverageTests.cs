using AngryMonkey.Cloud.Geography;
using AngryMonkey.CloudCommerce;

namespace CloudCommerce.Tests;

public class AdditionalCoverageTests
{
    [Fact]
    public async Task CalculateTotalsAsync_shipping_currency_mismatch_throws()
    {
        CommerceService service = Service();
        Cart cart = await service.CreateCartAsync("USD");
        await service.AddItemAsync(cart.Id, new("one", "One", new Money("USD", 10m)));
        ShippingSelection shipping = new("carrier", "service", new Money("EUR", 3m), Guid.NewGuid());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CalculateTotalsAsync(cart.Id, shipping));
    }

    [Fact]
    public async Task CheckoutAsync_without_integrations_creates_pending_order()
    {
        CommerceService service = Service();
        Cart cart = await service.CreateCartAsync("USD");
        await service.AddItemAsync(cart.Id, new("one", "One", new Money("USD", 10m)));

        Order order = await service.CheckoutAsync(new() { CartId = cart.Id, IdempotencyKey = "headless" });

        Assert.Equal(OrderStatuses.Pending, order.Status);
        Assert.Null(order.PaymentReference);
        Assert.Empty(order.InventoryReservationIds);
    }

    [Fact]
    public async Task RemoveItemAsync_unknown_item_preserves_cart()
    {
        CommerceService service = Service();
        Cart cart = await service.CreateCartAsync("USD");
        cart = await service.AddItemAsync(cart.Id, new("one", "One", new Money("USD", 10m)));

        Cart updated = await service.RemoveItemAsync(cart.Id, Guid.NewGuid());

        Assert.Equal(Assert.Single(cart.Items).Id, Assert.Single(updated.Items).Id);
    }

    [Fact]
    public async Task CalculateTotalsAsync_aggregates_multiple_discount_extensions()
    {
        CommerceService service = new(new InMemoryCartStore(), new InMemoryOrderStore(), [new FixedDiscount("A", 3m), new FixedDiscount("B", 2m)]);
        Cart cart = await service.CreateCartAsync("USD");
        await service.AddItemAsync(cart.Id, new("one", "One", new Money("USD", 20m)));

        CommerceTotals totals = await service.CalculateTotalsAsync(cart.Id);

        Assert.Equal(2, totals.Adjustments.Count);
        Assert.Equal(5m, totals.Discount.Value);
        Assert.Equal(15m, totals.Final.Value);
    }

    private static CommerceService Service() => new(new InMemoryCartStore(), new InMemoryOrderStore(), []);

    private sealed class FixedDiscount(string code, decimal value) : IDiscountProvider
    {
        public Task<IReadOnlyList<DiscountAdjustment>> CalculateAsync(Cart cart, Money itemsTotal, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DiscountAdjustment>>([new(code, $"Offer {code}", new Money(cart.Currency, value))]);
    }
}