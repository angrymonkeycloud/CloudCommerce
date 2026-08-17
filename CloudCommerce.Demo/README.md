# CloudCommerce developer demo

CloudCommerce.Demo is a runnable developer portal for the CloudCommerce ecosystem. It clearly separates UI shipped in `AngryMonkey.CloudCommerce.Components` from application-only documentation and test scaffolding.

## Run locally

```powershell
dotnet run --project CloudCommerce.Demo/CloudCommerce.Demo.csproj
```

## Public component reference

The component catalog at `/components` lists every shipped Razor component. Each component has a dedicated route such as `/components/product-card` or `/components/payment-selector` with:

- a **PACKAGE COMPONENT** identity, package name, namespace, and source link;
- a live rendering of the real Razor Class Library component;
- public parameters and callbacks;
- an explicitly labeled **DEMO-ONLY EVENT INSPECTOR**;
- copyable application integration code.

The current catalog covers `ProductCard`, `ProductInformation`, `CartView`, `CouponInput`, `PaymentSelector`, `ShippingSelector`, `BookingSelector`, `ShipmentTracker`, `CheckoutProgress`, `OrderSummary`, `SearchFilter`, and `ProviderCard`.

## Real payment sandbox labs

The payment index at `/payments` links to one dedicated lab per adapter:

| Provider | Lab route | Official test resources |
| --- | --- | --- |
| Stripe | `/payments/stripe` | [Stripe testing](https://docs.stripe.com/testing) |
| PayPal | `/payments/paypal` | [PayPal sandbox](https://developer.paypal.com/tools/sandbox/) |
| Adyen | `/payments/adyen` | [Adyen test cards](https://docs.adyen.com/development-resources/test-cards-and-credentials) |
| MyFatoorah | `/payments/myfatoorah` | [MyFatoorah test cards](https://docs.myfatoorah.com/docs/test-cards) |
| SkipCash | `/payments/skipcash` | [SkipCash integration manual](https://skipcash.app/assets/doc/SkipCashIntegrationManual.pdf) |
| Tap | `/payments/tap` | [Tap testing cards](https://developers.tap.company/reference/testing-cards) |
| PayTabs | `/payments/paytabs` | [PayTabs test cards](https://support.paytabs.com/en/support/solutions/articles/60000712315-what-are-the-test-cards-available-to-perform-payments-) |

A lab is disabled until its provider-issued test credentials are present. The page supplies exact `dotnet user-secrets` commands. After restarting the demo, **Create real test payment** calls the actual adapter test endpoint through `IPaymentService`. The lab displays normalized status, IDs, provider reference, timing, errors, hosted checkout action, and provider status refresh.

Credentials are never entered into or rendered by the Razor UI. Never configure live credentials in this demo.

## Other working journeys

- `/storefront`: live cart, quantities, coupon, shipping, booking, payment selection, and checkout orchestration using the local safe driver.
- `/booking`: real in-memory availability, capacity, reservation, confirmation, rescheduling, cancellation, and capacity release.
- `/logistics`: carrier-neutral rates, provider selection, shipment creation, and tracking progression.
- `/architecture`: package boundaries and dependency direction.

## Theme and assets

The theme button switches light/dark tokens across both the demo and package components and persists the preference in local storage. Styles are authored in `src/css/demo.less`; browser helpers are authored in `src/js/demo.js`; CloudMate generates static output under `wwwroot`.

See [CloudPayments](../CloudPayments/README.md), [CloudLogistics](../CloudLogistics/README.md), [CloudBooking](../CloudBooking/README.md), [CloudCommerce](../CloudCommerce/README.md), and [CloudCommerce Components](../CloudCommerce.Components/README.md).

CloudCommerce Demo is part of [Angry Monkey Cloud](https://angrymonkeycloud.com). Development follows the shared [AI instructions](https://github.com/angrymonkeycloud/CloudDocs/blob/main/docs/ai/instructions.md).