# Explore the CloudCommerce ecosystem demo

CloudCommerce Demo is a multi-page Blazor application that demonstrates application-owned catalog data, cart and pricing composition, provider-neutral payments, inventory and shipping, standalone booking, replaceable UI, and final order orchestration in one host.

## Start the demo

```powershell
dotnet run --project CloudCommerce.Demo
```

The default configuration uses deterministic in-memory payment, shipping, inventory, booking, cart, order, discount, and tax implementations. No database, cloud account, or external provider credential is required.

## Demo pages

- **Overview** presents the independent domains, reusable foundations, and safe-mode status.
- **Storefront** runs an end-to-end discover, cart, coupon, shipping, booking, payment-selection, and order journey.
- **Payments** compares Stripe, PayPal, Adyen, MyFatoorah, SkipCash, Tap Payments, and PayTabs, links to official sandbox/test-card resources, and simulates normalized lifecycle states.
- **Logistics** compares Aramex, DHL Express, and FedEx, then simulates inventory, rate selection, label creation, transit, final-mile, and delivery tracking.
- **Booking** uses the real CloudBooking in-memory services to calculate slots, reserve capacity, confirm, reschedule, cancel, and return capacity.
- **Components** showcases the default responsive components, design tokens, and semantic override slots.
- **Architecture** documents dependency direction, domain ownership, shared libraries, and the private CDM boundary.

## Enable provider sandboxes

Use .NET user secrets or environment variables for Stripe, PayPal, Adyen, MyFatoorah, SkipCash, Tap, PayTabs, Aramex, DHL Express, and FedEx configuration. When a provider’s minimum configuration exists, the demo registers its adapter alongside the safe local drivers.

The public demo does not send the interactive simulator or storefront transaction to those adapters. Production applications must use provider-hosted fields or client SDKs for sensitive payment details and pass only provider references or tokens to CloudPayments.

## Styling and customization

Demo styles are authored in `CloudCommerce.Demo/src/css/demo.less` and generated with CloudMate. Reusable package styles are authored in `CloudCommerce.Components/src/css/cloud-commerce.less`. Do not hand-edit generated CSS.

The component gallery demonstrates semantic slots for product, cart, checkout, payment, shipping, booking, tracking, provider cards, pages, and storefront composition. Replacing a Razor component does not replace the underlying commerce service.

## Booking persistence boundary

The demo intentionally does not add Azure Table Storage. CloudBooking remains database agnostic through `IBookingStore`; applications may supply a private CDM, Cosmos, SQL, or other adapter independently.

See [CloudCommerce](index.md), [UI customization](ui.md), [payment providers](../../CloudPayments/docs/providers.md), [shipping carriers](../../CloudLogistics/docs/carriers.md), and [booking](../../CloudBooking/docs/index.md).