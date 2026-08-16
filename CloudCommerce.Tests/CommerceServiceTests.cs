using AngryMonkey.Cloud.Geography;
using AngryMonkey.CloudCommerce;

namespace CloudCommerce.Tests;

public class CommerceServiceTests
{
    [Fact]
    public async Task CalculateTotalsAsync_discount_shipping_and_tax_produce_final_amount()
    {
        CommerceService service = new(new InMemoryCartStore(), new InMemoryOrderStore(), [new FixedDiscount()], new FixedTax());
        Cart cart = await service.CreateCartAsync("USD");
        await service.AddItemAsync(cart.Id, new("item", "Item", new Money("USD", 100m)));
        ShippingSelection shipping = new("carrier", "standard", new Money("USD", 5m), Guid.NewGuid());

        CommerceTotals totals = await service.CalculateTotalsAsync(cart.Id, shipping);

        Assert.Equal(100m, totals.Items.Value);
        Assert.Equal(10m, totals.Discount.Value);
        Assert.Equal(5m, totals.Shipping.Value);
        Assert.Equal(19m, totals.Tax.Value);
        Assert.Equal(114m, totals.Final.Value);
    }

    private sealed class FixedDiscount : IDiscountProvider
    {
        public Task<IReadOnlyList<DiscountAdjustment>> CalculateAsync(Cart cart, Money itemsTotal, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DiscountAdjustment>>([new("SAVE10", "Ten off", new Money(cart.Currency, 10m))]);
    }

    private sealed class FixedTax : ICommerceTaxProvider
    {
        public Task<Money> CalculateAsync(Cart cart, Money taxableAmount, ShippingSelection? shipping, CancellationToken cancellationToken = default)
            => Task.FromResult(new Money(cart.Currency, 19m));
    }
}
