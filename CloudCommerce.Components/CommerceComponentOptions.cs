using AngryMonkey.CloudCommerce.Components.Components;
using AngryMonkey.CloudCommerce.Components.Pages;
using Microsoft.AspNetCore.Components;

namespace AngryMonkey.CloudCommerce.Components;

public enum CommerceComponentSlots
{
    ProductCard,
    ProductInformation,
    CartItem,
    Cart,
    Checkout,
    CouponInput,
    ShippingSelector,
    PaymentSelector,
    OrderSummary,
    SearchFilter,
    CatalogPage,
    ProductPage,
    CartPage,
    CheckoutPage,
    OrderConfirmationPage,
    AccountPage,
    OrdersPage,
    Storefront,
    CheckoutProgress,
    BookingSelector,
    ShipmentTracker,
    ProviderCard
}

public sealed class CommerceComponentOptions
{
    private readonly Dictionary<CommerceComponentSlots, Type> _components = new()
    {
        [CommerceComponentSlots.ProductCard] = typeof(ProductCard),
        [CommerceComponentSlots.ProductInformation] = typeof(ProductInformation),
        [CommerceComponentSlots.CartItem] = typeof(CartItemView),
        [CommerceComponentSlots.Cart] = typeof(CartView),
        [CommerceComponentSlots.Checkout] = typeof(CheckoutView),
        [CommerceComponentSlots.CouponInput] = typeof(CouponInput),
        [CommerceComponentSlots.ShippingSelector] = typeof(ShippingSelector),
        [CommerceComponentSlots.PaymentSelector] = typeof(PaymentSelector),
        [CommerceComponentSlots.OrderSummary] = typeof(OrderSummary),
        [CommerceComponentSlots.SearchFilter] = typeof(SearchFilter),
        [CommerceComponentSlots.CatalogPage] = typeof(CatalogPage),
        [CommerceComponentSlots.ProductPage] = typeof(ProductPage),
        [CommerceComponentSlots.CartPage] = typeof(CartPage),
        [CommerceComponentSlots.CheckoutPage] = typeof(CheckoutPage),
        [CommerceComponentSlots.OrderConfirmationPage] = typeof(OrderConfirmationPage),
        [CommerceComponentSlots.AccountPage] = typeof(AccountPage),
        [CommerceComponentSlots.OrdersPage] = typeof(OrdersPage),
        [CommerceComponentSlots.Storefront] = typeof(CloudStorefront),
        [CommerceComponentSlots.CheckoutProgress] = typeof(CheckoutProgress),
        [CommerceComponentSlots.BookingSelector] = typeof(BookingSelector),
        [CommerceComponentSlots.ShipmentTracker] = typeof(ShipmentTracker),
        [CommerceComponentSlots.ProviderCard] = typeof(ProviderCard)
    };

    public CommerceComponentOptions Replace<TComponent>(CommerceComponentSlots slot) where TComponent : IComponent
    {
        _components[slot] = typeof(TComponent);
        return this;
    }

    public Type Get(CommerceComponentSlots slot) => _components[slot];
}
