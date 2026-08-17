# CloudPayments provider-neutral payment orchestration

CloudPayments processes caller-supplied monetary amounts through replaceable payment providers. It works independently of commerce, login, logistics, booking, and CDM.

## CloudPayments responsibilities

The public contracts cover payment creation, authorization, capture, void, full or partial refunds, recurring charges, saved payment-method references, status retrieval, provider references, idempotency, normalized errors, and validated provider webhooks.

`IPaymentProvider` isolates provider-specific SDKs. `IPaymentProviderRegistry` selects a registered adapter by name. `IPaymentService` enforces capabilities, idempotency, optional tax calculation, and persistence through `IPaymentStore`.

## What CloudPayments does not own

CloudPayments does not own carts, coupons, promotions, product pricing, shipping prices, or offer rules. A caller computes the final amount and CloudPayments processes it. Optional tax calculation is disabled by default and delegates to `ITaxProvider`; no jurisdiction rules are hard-coded.

## Registering CloudPayments

```csharp
services.AddCloudPayments(options => options.TaxCalculationEnabled = false);
services.AddCloudPaymentProvider<MyPaymentProvider>();
```

Provider packages follow the `CloudPayments.ProviderName` boundary and implement only the capabilities they advertise. The first adapters are [Stripe, PayPal, and Adyen](providers.md).

## Testing payment providers

Unit tests use fake providers and the in-memory stores. Provider adapter tests should independently validate request mapping, error normalization, idempotency forwarding, signature verification, and webhook parsing without loading CloudCommerce.

See the [ecosystem architecture](../../README.md) and [CloudCommerce orchestration](../../CloudCommerce/docs/index.md).
