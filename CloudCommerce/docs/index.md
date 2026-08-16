# CloudCommerce headless cart, checkout, and order orchestration

CloudCommerce is the optional composition layer for applications that need carts, checkout, orders, discounts, tax orchestration, shipping selection, payments, inventory reservations, or booking-related commerce workflows.

## Commerce responsibilities

Checkout uses compensating operations when a later step fails: inventory reservations and booking reservations are released, and a captured payment is refunded or an authorization is voided through CloudPayments.

`ICommerceService` manages active carts, immutable order snapshots, totals, discounts, coupons, checkout idempotency, optional inventory reservations, and optional payment execution. `ICommerceTaxProvider`, `IDiscountProvider`, `ICommercePaymentGateway`, and `ICommerceLogisticsGateway` keep application rules and specialized providers outside the orchestration service.

## Lightweight product references

`ProductPresentation` contains only display and checkout snapshot information: identifier, title, description, image, price, currency, reference, and metadata. It is not an authoritative product entity. Applications resolve and validate their own products through `IProductResolver` before adding them to a cart.

## Headless registration

```csharp
services.AddCloudPayments();
services.AddCloudLogistics();
services.AddCloudBooking();
services.AddCloudCommerce()
        .AddCloudCommercePayments()
        .AddCloudCommerceLogistics()
        .AddCloudCommerceBooking();
```

Applications may register only `AddCloudCommerce()` for cart/order workflows that do not execute payments or reserve physical inventory.

## Persistence boundary

`ICartStore` and `IOrderStore` are public database-agnostic contracts. In-memory defaults support local use and isolated tests. The private CDM adapter replaces those stores in CDM hosts; CloudCommerce never references CDM.

## What CloudCommerce does not own

CloudCommerce does not own payment-provider SDKs, carrier SDKs, authoritative products, a universal subscription engine, geography datasets, identity memberships, CDM forms, grids, dashboards, Cosmos, or Azure Storage.

See the [CloudCommerce demo](demo.md), [CloudCommerce UI customization](ui.md), the [ecosystem architecture](../../docs/commerce-ecosystem/index.md), [CloudPayments](../../CloudPayments/docs/index.md), [CloudLogistics](../../CloudLogistics/docs/index.md), and [CloudBooking](../../CloudBooking/docs/index.md).
