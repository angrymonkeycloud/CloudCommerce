# CloudPayments provider adapters and sandbox resources

CloudPayments translates one provider-neutral transaction lifecycle into independently packaged payment adapters. Applications can install only the providers they need; no adapter depends on CloudCommerce, CloudLogin, CDM, booking, or logistics.

## Supported payment providers

| Package | Primary fit | Implemented foundation | Official testing resources |
| --- | --- | --- | --- |
| `AngryMonkey.CloudPayments.Stripe` | Global | PaymentIntents, authorization, capture, cancel, refunds, saved references, recurring charges, signed webhooks | [Stripe test mode and cards](https://docs.stripe.com/testing) |
| `AngryMonkey.CloudPayments.PayPal` | Global | Orders v2, approval redirects, capture, authorization void, capture refunds, billing references, OAuth, webhook verification | [PayPal sandbox guide](https://developer.paypal.com/tools/sandbox/) · [card testing](https://developer.paypal.com/sandbox-testing/card-testing) |
| `AngryMonkey.CloudPayments.Adyen` | Global enterprise | Checkout payments, actions, stored references, recurring charges, capture, cancel, refund, HMAC webhooks | [Adyen test cards and credentials](https://docs.adyen.com/development-resources/test-cards-and-credentials) |
| `AngryMonkey.CloudPayments.MyFatoorah` | MENA | Hosted create/authorize, invoice lookup, capture/release, refund requests, signed webhook events | [MyFatoorah sandbox setup](https://docs.myfatoorah.com/docs/get-started) · [test cards](https://docs.myfatoorah.com/docs/test-cards) |
| `AngryMonkey.CloudPayments.SkipCash` | Qatar | QAR hosted checkout, ordered HMAC request signing, transaction lookup, status mapping, signed callbacks | [SkipCash integration manual](https://skipcash.app/assets/doc/SkipCashIntegrationManual.pdf) · [merchant portal](https://merchantportal.skipcash.app/) |
| `AngryMonkey.CloudPayments.Tap` | MENA | Charges, authorization, capture, void, full/partial refunds, saved sources, webhook hash validation | [Tap get started](https://developers.tap.company/docs/get-started) · [testing cards](https://developers.tap.company/reference/testing-cards) |
| `AngryMonkey.CloudPayments.PayTabs` | MENA | Hosted sale/authorization, capture, void, full/partial refund, transaction query, signed callbacks/IPN | [PayTabs hosted payment API](https://support.paytabs.com/en/support/solutions/articles/60000992876-3-2-1-hosted-payment-page-apis-initiating-the-payment) · [test cards](https://support.paytabs.com/en/support/solutions/articles/60000712315-what-are-the-test-cards-available-to-perform-payments-) |

## Register an adapter

Each package exposes one focused dependency-injection extension. The example below targets the MyFatoorah test environment by default:

```csharp
services.AddCloudPayments();
services.AddMyFatoorahCloudPayments(options =>
{
    options.ApiToken = configuration["MyFatoorah:ApiToken"]!;
    options.WebhookSecret = configuration["MyFatoorah:WebhookSecret"]!;
    options.PaymentMethodId = configuration.GetValue<int>("MyFatoorah:PaymentMethodId");
});
```

Use user secrets, environment variables, or a managed secret store. Never commit API tokens, HMAC secrets, server keys, webhook secrets, test buyer credentials, or live credentials.

## Capability differences

Provider capability flags are deliberate. CloudPayments checks capabilities before invoking an operation and throws a clear `NotSupportedException` instead of pretending an unsupported operation succeeded. Hosted providers commonly return `PaymentStatuses.RequiresAction` with a redirect. Refund requests can remain pending when the provider performs asynchronous review.

## Safe local testing

Core and adapter unit tests use deterministic HTTP handlers and never call a real sandbox. The CloudCommerce demo adds a no-credentials state simulator that presents every provider and links to its official testing material. Supplying local sandbox credentials registers the real adapter alongside the safe demo driver; it does not cause the public demo to transmit card data.

See [CloudPayments](index.md), the [CloudCommerce demo](../../CloudCommerce/docs/demo.md), [shipping carriers](../../CloudLogistics/docs/carriers.md), and the [commerce ecosystem architecture](../../docs/commerce-ecosystem/index.md).