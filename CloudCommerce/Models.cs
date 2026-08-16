using AngryMonkey.Cloud.Geography;
using AngryMonkey.CloudLogistics;
using AngryMonkey.CloudPayments;

namespace AngryMonkey.CloudCommerce;

public enum CartStatuses
{
    Active,
    Converted,
    Abandoned
}

public enum OrderStatuses
{
    Pending,
    AwaitingPayment,
    Paid,
    Processing,
    PartiallyFulfilled,
    Fulfilled,
    Cancelled,
    Refunded
}

public enum DiscountTypes
{
    FixedAmount,
    Percentage,
    ApplicationDefined
}

/// <summary>A non-authoritative representation. The application continues to own the actual product or service entity.</summary>
public sealed record ProductPresentation(string Id, string Title, Money Price, string? Description = null, Uri? Image = null, string? Reference = null, Dictionary<string, string>? Metadata = null);

public sealed record CommerceAccountReference(Guid? UserId = null, Guid? OrganizationId = null);

public sealed class CartItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required ProductPresentation Product { get; init; }
    public int Quantity { get; init; } = 1;
    public Dictionary<string, string> Metadata { get; init; } = [];
}

public sealed class Cart
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public CommerceAccountReference? Account { get; init; }
    public required string Currency { get; init; }
    public CartStatuses Status { get; init; } = CartStatuses.Active;
    public IReadOnlyList<CartItem> Items { get; init; } = [];
    public IReadOnlyList<string> CouponCodes { get; init; } = [];
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record DiscountAdjustment(string Code, string Description, Money Amount, DiscountTypes Type = DiscountTypes.ApplicationDefined);

public sealed class CommerceTotals
{
    public required Money Items { get; init; }
    public required Money Discount { get; init; }
    public required Money Shipping { get; init; }
    public required Money Tax { get; init; }
    public required Money Final { get; init; }
    public IReadOnlyList<DiscountAdjustment> Adjustments { get; init; } = [];
}

public sealed record ShippingSelection(string Provider, string ServiceCode, Money Price, Guid WarehouseId, LogisticsAddress? Address = null);

public sealed class CheckoutRequest
{
    public required Guid CartId { get; init; }
    public required string IdempotencyKey { get; init; }
    public string? PaymentProvider { get; init; }
    public PaymentMethodReference? PaymentMethod { get; init; }
    public ShippingSelection? Shipping { get; init; }
    public CommerceAccountReference? Account { get; init; }
    public IReadOnlyList<Guid> BookingReservationIds { get; init; } = [];
    public Dictionary<string, string> Metadata { get; init; } = [];
}

public sealed record OrderItem(Guid Id, ProductPresentation Product, int Quantity, Money UnitPrice, Dictionary<string, string>? Metadata = null);

public sealed class Order
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid CartId { get; init; }
    public CommerceAccountReference? Account { get; init; }
    public required IReadOnlyList<OrderItem> Items { get; init; }
    public required CommerceTotals Totals { get; init; }
    public OrderStatuses Status { get; init; }
    public string? PaymentProvider { get; init; }
    public string? PaymentReference { get; init; }
    public PaymentStatuses? PaymentStatus { get; init; }
    public PaymentAction? PaymentAction { get; init; }
    public IReadOnlyList<Guid> InventoryReservationIds { get; init; } = [];
    public ShippingSelection? Shipping { get; init; }
    public IReadOnlyList<Guid> BookingReservationIds { get; init; } = [];
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public Dictionary<string, string> Metadata { get; init; } = [];
}
