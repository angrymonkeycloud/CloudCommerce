using AngryMonkey.CloudLogistics;

namespace CloudLogistics.Tests;

public class InventoryServiceTests
{
    [Fact]
    public async Task ReserveAsync_available_stock_reduces_availability_and_release_restores_it()
    {
        InventoryService service = new(new InMemoryInventoryStore());
        InventoryItemReference item = new("sku-1");
        Guid warehouseId = Guid.NewGuid();
        await service.SetStockAsync(item, warehouseId, 5);

        InventoryReservation reservation = await service.ReserveAsync(item, warehouseId, 2, TimeSpan.FromMinutes(5));
        Assert.Equal(3, (await service.GetAvailabilityAsync(item, warehouseId)).Available);

        await service.ReleaseAsync(reservation.Id);
        Assert.Equal(5, (await service.GetAvailabilityAsync(item, warehouseId)).Available);
    }
}
