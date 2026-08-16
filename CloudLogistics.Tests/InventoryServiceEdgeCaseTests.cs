using AngryMonkey.Cloud;
using AngryMonkey.CloudLogistics;

namespace CloudLogistics.Tests;

public class InventoryServiceEdgeCaseTests
{
    [Fact]
    public async Task SetStockAsync_negative_value_throws()
    {
        InventoryService service = new(new InMemoryInventoryStore());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.SetStockAsync(new("sku"), Guid.NewGuid(), -1));
    }

    [Fact]
    public async Task ReserveAsync_insufficient_inventory_throws()
    {
        InventoryService service = new(new InMemoryInventoryStore());
        Guid warehouse = Guid.NewGuid();
        InventoryItemReference item = new("sku");
        await service.SetStockAsync(item, warehouse, 1);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReserveAsync(item, warehouse, 2, TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public async Task SetStockAsync_cannot_drop_below_reserved()
    {
        InventoryService service = new(new InMemoryInventoryStore());
        Guid warehouse = Guid.NewGuid();
        InventoryItemReference item = new("sku");
        await service.SetStockAsync(item, warehouse, 5);
        await service.ReserveAsync(item, warehouse, 3, TimeSpan.FromMinutes(5));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetStockAsync(item, warehouse, 2));
    }

    [Fact]
    public async Task AdjustAsync_cannot_reduce_available_below_zero()
    {
        InventoryService service = new(new InMemoryInventoryStore());
        Guid warehouse = Guid.NewGuid();
        InventoryItemReference item = new("sku");
        await service.SetStockAsync(item, warehouse, 2);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AdjustAsync(item, warehouse, -3));
    }

    [Fact]
    public async Task TransferAsync_moves_stock_between_warehouses()
    {
        InventoryService service = new(new InMemoryInventoryStore());
        Guid source = Guid.NewGuid();
        Guid destination = Guid.NewGuid();
        InventoryItemReference item = new("sku");
        await service.SetStockAsync(item, source, 5);

        await service.TransferAsync(new(Guid.NewGuid(), item, source, destination, 2, DateTimeOffset.UtcNow));

        Assert.Equal(3, (await service.GetAvailabilityAsync(item, source)).OnHand);
        Assert.Equal(2, (await service.GetAvailabilityAsync(item, destination)).OnHand);
    }

    [Fact]
    public async Task ReleaseAsync_unknown_reservation_is_idempotent()
    {
        InventoryService service = new(new InMemoryInventoryStore());
        await service.ReleaseAsync(Guid.NewGuid());
    }

    [Fact]
    public void Shipping_registry_lookup_is_case_insensitive()
    {
        StubShippingProvider provider = new();
        Assert.Same(provider, new ShippingProviderRegistry([provider]).Get("STUB"));
    }

    [Fact]
    public void Address_validator_rejects_unknown_country()
    {
        CloudGeographyAddressValidator validator = new(new CloudGeographyClient());
        GeographyValidationResult result = validator.Validate(new() { CountryCode = "ZZ", Locality = "Nowhere", Line1 = "Unknown" });
        Assert.False(result.IsValid);
    }

    private sealed class StubShippingProvider : IShippingProvider
    {
        public string Name => "stub";
        public ShippingProviderCapabilities Capabilities => ShippingProviderCapabilities.None;
        public Task<IReadOnlyList<ShippingRate>> GetRatesAsync(ShippingRateRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Shipment> CreateShipmentAsync(ShipmentRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Shipment?> GetShipmentAsync(Guid shipmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Shipment> RefreshTrackingAsync(Guid shipmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CancelShipmentAsync(Guid shipmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<PickupLocation>> GetPickupLocationsAsync(string countryCode, string? subdivisionCode = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
