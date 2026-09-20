namespace AngryMonkey.CloudCommerce.Demo;

public sealed record ComponentReferenceDefinition(string Slug, string Tag, string Title, string Category, string Description, IReadOnlyList<string> Parameters, IReadOnlyList<string> Events);

public static class ComponentReferenceCatalog
{
    public const string PackageName = "AngryMonkey.CloudCommerce.Components";
    public const string Namespace = "AngryMonkey.CloudCommerce.Components.Components";

    public static IReadOnlyList<ComponentReferenceDefinition> All { get; } =
    [
        new("product-card", "ProductCard", "Product card", "Catalog presentation", "Compact application-owned product presentation with a strongly typed add callback.", ["Product"], ["OnAdd"]),
        new("product-information", "ProductInformation", "Product information", "Catalog presentation", "Full product detail surface without introducing an authoritative product model.", ["Product"], ["OnAdd"]),
        new("cart-view", "CartView", "Cart view", "Cart", "Cart summary composed from reusable CartItemView rows.", ["Cart", "Title"], ["OnRemove"]),
        new("coupon-input", "CouponInput", "Coupon input", "Cart", "Accessible promotion-code input that emits the submitted code.", [], ["OnApply"]),
        new("payment-selector", "PaymentSelector", "Payment selector", "Checkout", "Provider choice UI that emits a provider name without executing payment logic in markup.", ["Options", "Providers", "SelectedProvider", "Title", "Description"], ["OnSelected"]),
        new("shipping-selector", "ShippingSelector", "Shipping selector", "Checkout", "Normalized delivery-rate selection independent of carrier payloads.", ["Options", "Selected", "Title", "Description"], ["OnSelected"]),
        new("booking-selector", "BookingSelector", "Booking selector", "Booking", "Availability and time-slot selection over standalone CloudBooking models.", ["Slots", "Selected", "TimeZoneLabel", "Title", "Eyebrow", "Description"], ["OnSelected"]),
        new("shipment-tracker", "ShipmentTracker", "Shipment tracker", "Logistics", "Carrier-neutral shipment state, tracking reference, and event timeline.", ["Shipment", "Events", "Title"], []),
        new("checkout-progress", "CheckoutProgress", "Checkout progress", "Checkout", "A responsive progress indicator for application-defined checkout steps.", ["Steps"], []),
        new("order-summary", "OrderSummary", "Order summary", "Checkout", "Items, discount, shipping, tax, adjustments, and final total presentation.", ["Totals"], []),
        new("search-filter", "SearchFilter", "Search filter", "Catalog", "Accessible search input with two-way query callback and clear action.", ["Query", "Placeholder"], ["QueryChanged"]),
        new("provider-card", "ProviderCard", "Provider card", "Integration", "Provider capability presentation with honest navigation or explicit selection behavior.", ["Model", "ActionHref", "ActionLabel", "Selected", "SelectedLabel", "ChildContent"], ["OnSelected"]),
        new("cart-item-view", "CartItemView", "Cart item", "Cart", "An individual cart row with quantity, price and remove callback.", ["Item"], ["OnRemove"]),
        new("checkout-view", "CheckoutView", "Checkout view", "Checkout", "Composable checkout shell with cart, totals, progress and application content.", ["Cart", "Totals", "Title", "Description", "Steps", "ChildContent"], []),
        new("commerce-component-host", "CommerceComponentHost", "Component host", "Composition", "Resolve a component slot through the registered component registry.", ["Slot", "Parameters"], [])
    ];

    public static ComponentReferenceDefinition? Find(string? slug) => All.FirstOrDefault(item => item.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase));
    public static int IndexOf(ComponentReferenceDefinition definition) => All.ToList().IndexOf(definition);
    public static string SourceUrl(ComponentReferenceDefinition definition) => $"https://github.com/angrymonkeycloud/CloudCommerce/blob/main/CloudCommerce.Components/Components/{definition.Tag}.razor";
}
