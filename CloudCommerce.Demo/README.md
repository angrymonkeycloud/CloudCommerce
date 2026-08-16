# CloudCommerce ecosystem demo

A professional, runnable Blazor showcase for CloudCommerce, CloudPayments, CloudLogistics, and CloudBooking. It includes a complete storefront, seven payment-provider learning cards, three carrier integrations, a real standalone booking lifecycle, a component gallery, and an architecture map.

## Run locally

```powershell
dotnet run --project CloudCommerce.Demo
```

The demo is safe without credentials: payment/provider calls and carrier movement are simulated locally, while cart, totals, tax, discounts, inventory reservations, booking availability, booking lifecycle, and final commerce orchestration use the real public services with in-memory implementations.

Provider sandbox adapters become registered when their configuration is supplied through user secrets or environment variables. Never commit provider credentials. See the [complete demo guide](../CloudCommerce/docs/demo.md), [payment-provider resources](../CloudPayments/docs/providers.md), and [carrier resources](../CloudLogistics/docs/carriers.md).

The application intentionally does not use Azure Table Storage and does not add a persistence dependency to CloudBooking.