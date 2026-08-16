namespace AngryMonkey.CloudLogistics;

public interface IInventoryStore
{
    Task<InventoryLevel?> GetAsync(InventoryItemReference item, Guid warehouseId, CancellationToken cancellationToken = default);
    Task SaveAsync(InventoryLevel level, CancellationToken cancellationToken = default);
    Task SaveReservationAsync(InventoryReservation reservation, CancellationToken cancellationToken = default);
    Task<InventoryReservation?> GetReservationAsync(Guid reservationId, CancellationToken cancellationToken = default);
    Task DeleteReservationAsync(Guid reservationId, CancellationToken cancellationToken = default);
    Task SaveMovementAsync(StockMovement movement, CancellationToken cancellationToken = default);
}

public interface IInventoryService
{
    Task<InventoryLevel> GetAvailabilityAsync(InventoryItemReference item, Guid warehouseId, CancellationToken cancellationToken = default);
    Task<InventoryLevel> SetStockAsync(InventoryItemReference item, Guid warehouseId, int onHand, CancellationToken cancellationToken = default);
    Task<InventoryReservation> ReserveAsync(InventoryItemReference item, Guid warehouseId, int quantity, TimeSpan duration, string? ownerReference = null, CancellationToken cancellationToken = default);
    Task ReleaseAsync(Guid reservationId, CancellationToken cancellationToken = default);
    Task<InventoryLevel> AdjustAsync(InventoryItemReference item, Guid warehouseId, int quantity, string? reference = null, CancellationToken cancellationToken = default);
    Task TransferAsync(StockTransfer transfer, CancellationToken cancellationToken = default);
}

public interface IFulfillmentService
{
    Task<Fulfillment> CreateAsync(IReadOnlyList<FulfillmentLine> lines, CancellationToken cancellationToken = default);
    Task<Fulfillment> PickAsync(Guid fulfillmentId, IReadOnlyList<FulfillmentLine> lines, CancellationToken cancellationToken = default);
    Task<Fulfillment> PackAsync(Guid fulfillmentId, CancellationToken cancellationToken = default);
    Task<Fulfillment> PrepareShipmentAsync(Guid fulfillmentId, ShipmentRequest request, CancellationToken cancellationToken = default);
}

public interface IShippingProvider
{
    string Name { get; }
    ShippingProviderCapabilities Capabilities { get; }
    Task<IReadOnlyList<ShippingRate>> GetRatesAsync(ShippingRateRequest request, CancellationToken cancellationToken = default);
    Task<Shipment> CreateShipmentAsync(ShipmentRequest request, CancellationToken cancellationToken = default);
    Task<Shipment?> GetShipmentAsync(Guid shipmentId, CancellationToken cancellationToken = default);
    Task<Shipment> RefreshTrackingAsync(Guid shipmentId, CancellationToken cancellationToken = default);
    Task CancelShipmentAsync(Guid shipmentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PickupLocation>> GetPickupLocationsAsync(string countryCode, string? subdivisionCode = null, CancellationToken cancellationToken = default);
}

public interface IShippingProviderRegistry
{
    IReadOnlyCollection<IShippingProvider> Providers { get; }
    IShippingProvider Get(string name);
}

public interface IGeographicAddressValidator
{
    GeographyValidationResult Validate(LogisticsAddress address);
}
