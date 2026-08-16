using AngryMonkey.CloudLogistics;

namespace CloudLogistics.Tests;

public class AdditionalCoverageTests
{
    [Fact]
    public async Task ReserveAsync_zero_duration_throws_without_changing_stock()
    {
        InventoryService service = new(new InMemoryInventoryStore());
        Guid warehouse = Guid.NewGuid();
        InventoryItemReference item = new("sku");
        await service.SetStockAsync(item, warehouse, 4);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.ReserveAsync(item, warehouse, 1, TimeSpan.Zero));

        Assert.Equal(4, (await service.GetAvailabilityAsync(item, warehouse)).Available);
    }

    [Fact]
    public async Task Concurrent_reservations_cannot_oversell_inventory()
    {
        InventoryService service = new(new InMemoryInventoryStore());
        Guid warehouse = Guid.NewGuid();
        InventoryItemReference item = new("limited");
        await service.SetStockAsync(item, warehouse, 1);

        Task<InventoryReservation>[] attempts = [service.ReserveAsync(item, warehouse, 1, TimeSpan.FromMinutes(5)), service.ReserveAsync(item, warehouse, 1, TimeSpan.FromMinutes(5))];
        Exception? failure = await Record.ExceptionAsync(() => Task.WhenAll(attempts));

        Assert.IsType<InvalidOperationException>(failure);
        Assert.Equal(0, (await service.GetAvailabilityAsync(item, warehouse)).Available);
    }

    [Fact]
    public async Task Variant_inventory_levels_are_isolated()
    {
        InventoryService service = new(new InMemoryInventoryStore());
        Guid warehouse = Guid.NewGuid();
        InventoryItemReference small = new("shirt", "small");
        InventoryItemReference large = new("shirt", "large");
        await service.SetStockAsync(small, warehouse, 3);
        await service.SetStockAsync(large, warehouse, 8);
        await service.ReserveAsync(small, warehouse, 2, TimeSpan.FromMinutes(5));

        Assert.Equal(1, (await service.GetAvailabilityAsync(small, warehouse)).Available);
        Assert.Equal(8, (await service.GetAvailabilityAsync(large, warehouse)).Available);
    }

    [Fact]
    public async Task TransferAsync_non_positive_quantity_preserves_both_warehouses()
    {
        InventoryService service = new(new InMemoryInventoryStore());
        Guid source = Guid.NewGuid();
        Guid destination = Guid.NewGuid();
        InventoryItemReference item = new("sku");
        await service.SetStockAsync(item, source, 5);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.TransferAsync(new(Guid.NewGuid(), item, source, destination, 0, DateTimeOffset.UtcNow)));

        Assert.Equal(5, (await service.GetAvailabilityAsync(item, source)).OnHand);
        Assert.Equal(0, (await service.GetAvailabilityAsync(item, destination)).OnHand);
    }
}