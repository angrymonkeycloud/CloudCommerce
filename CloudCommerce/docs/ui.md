# Customize CloudCommerce Blazor components and storefronts

CloudCommerce Components provides a polished default Blazor UI while keeping business and application logic in headless services. Applications may adopt a full storefront, individual pages, individual components, or no UI.

## Component override system

Every default UI surface has a `CommerceComponentSlots` value. Replace a component during registration without replacing cart, checkout, order, payment, logistics, or booking services:

```csharp
services.AddCloudCommerceComponents(options =>
{
    options.Replace<MyProductCard>(CommerceComponentSlots.ProductCard);
    options.Replace<MyCheckout>(CommerceComponentSlots.Checkout);
    options.Replace<MyBookingCalendar>(CommerceComponentSlots.BookingSelector);
});
```

Default page compositions render nested surfaces through `CommerceComponentHost`, which uses Blazor `DynamicComponent`. Parameters and callbacks remain normal Blazor component APIs.

## Default component surfaces

The package includes:

- Product cards, product information, search/filter, catalog, and product pages.
- Cart, cart item, coupon, order summary, checkout progress, checkout, and confirmation surfaces.
- Rich payment-provider and shipping-rate selectors.
- Booking availability selection and shipment tracking timelines.
- Provider capability/resource cards used by documentation or integration experiences.
- Optional account, order-list, and full-storefront compositions.

## Adoption levels

1. Use `CloudStorefront` as a complete shell.
2. Render a supplied page composition independently.
3. Render one default component inside an application-owned page.
4. Replace selected semantic slots with application Razor components.
5. Use only `ICommerceService` for a fully headless application.

## Styling and theme tokens

Source styles live in `CloudCommerce.Components/src/css/cloud-commerce.less`; CloudMate generates `wwwroot/css/cloud-commerce.css`. Never hand-author or edit generated CSS.

Default class names use the `commerce-` block prefix. Applications can override the focused CSS custom-property surface, provide a wrapper theme through `data-commerce-theme`, extend styles in application-owned LESS, or replace component markup entirely.

See [CloudCommerce](index.md), the [interactive demo](demo.md), and the [ecosystem architecture](../../docs/commerce-ecosystem/index.md).