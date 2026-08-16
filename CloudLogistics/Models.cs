using AngryMonkey.Cloud.Geography;

namespace AngryMonkey.CloudLogistics;

public enum StockMovementTypes
{
    Receipt,
    Reservation,
    ReservationRelease,
    Adjustment,
    TransferOut,
    TransferIn,
    Fulfillment,
    Return
}

public enum FulfillmentStatuses
{
    Pending,
    Reserved,
    Picking,
    Packed,
    ReadyToShip,
    PartiallyFulfilled,
    Fulfilled,
    Cancelled
}

public enum ShipmentStatuses
{
    Pending,
    LabelCreated,
    InTransit,
    OutForDelivery,
    Delivered,
    DeliveryFailed,
    Returned,
    Cancelled
}

[Flags]
public enum ShippingProviderCapabilities
{
    None = 0,
    Rates = 1,
    Labels = 2,
    Tracking = 4,
    PickupLocations = 8,
    Cancellation = 16
}

/// <summary>
/// A postal address whose geographic codes are resolved and validated through CloudGeography.
/// CloudGeography owns the country and subdivision definitions; this type only adds delivery-specific address lines.
/// </summary>
public sealed record LogisticsAddress
{
    public required string CountryCode { get; init; }
    public string? SubdivisionCode { get; init; }
    public string? SubdivisionChildCode { get; init; }
    public required string Locality { get; init; }
    public string? PostalCode { get; init; }
    public required string Line1 { get; init; }
    public string? Line2 { get; init; }
    public string? RecipientName { get; init; }
    public string? PhoneNumber { get; init; }
}

public sealed record Warehouse(Guid Id, string Name, LogisticsAddress Address, bool IsActive = true);
public sealed record InventoryItemReference(string Reference, string? VariantReference = null, Dictionary<string, string>? Metadata = null);

public sealed class InventoryLevel
{
    public required InventoryItemReference Item { get; init; }
    public required Guid WarehouseId { get; init; }
    public int OnHand { get; init; }
    public int Reserved { get; init; }
    public int Available => OnHand - Reserved;
    public long Version { get; init; }
}

public sealed record InventoryReservation(Guid Id, InventoryItemReference Item, Guid WarehouseId, int Quantity, DateTimeOffset ExpiresAt, string? OwnerReference = null);
public sealed record StockMovement(Guid Id, InventoryItemReference Item, Guid WarehouseId, int Quantity, StockMovementTypes Type, DateTimeOffset OccurredAt, string? Reference = null);
public sealed record StockTransfer(Guid Id, InventoryItemReference Item, Guid FromWarehouseId, Guid ToWarehouseId, int Quantity, DateTimeOffset CreatedAt);

public sealed record FulfillmentLine(string ItemReference, int Quantity, Guid? InventoryReservationId = null);
public sealed record Fulfillment(Guid Id, IReadOnlyList<FulfillmentLine> Lines, FulfillmentStatuses Status, IReadOnlyList<Guid> ShipmentIds, DateTimeOffset UpdatedAt);

public sealed record ShippingRate(string Provider, string ServiceCode, string Name, Money Price, TimeSpan? EstimatedTransitTime = null);
public sealed record ShippingRateRequest(LogisticsAddress Origin, LogisticsAddress Destination, IReadOnlyList<ShippingPackage> Packages, Dictionary<string, string>? Metadata = null);
public sealed record ShippingPackage(decimal Weight, decimal Length, decimal Width, decimal Height, string WeightUnit = "kg", string LengthUnit = "cm");
public sealed record ShipmentRequest(LogisticsAddress Origin, LogisticsAddress Destination, IReadOnlyList<ShippingPackage> Packages, string ServiceCode, string? Reference = null);
public sealed record ShipmentDocument(string Type, string Format, string? Content = null, Uri? Url = null);
public sealed record Shipment(Guid Id, string Provider, string ServiceCode, ShipmentStatuses Status, string? TrackingNumber = null, Uri? TrackingUrl = null, Uri? LabelUrl = null, DateTimeOffset? DeliveredAt = null, string? ProviderReference = null, IReadOnlyList<ShipmentDocument>? Documents = null, Dictionary<string, string>? Metadata = null);
public sealed record PickupLocation(string Provider, string Code, string Name, LogisticsAddress Address);

public sealed record GeographyValidationResult(bool IsValid, string? Error = null);
