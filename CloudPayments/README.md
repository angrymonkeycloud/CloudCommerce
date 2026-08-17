# CloudPayments

CloudPayments is a standalone, provider-neutral payment orchestration library. It owns transaction execution, idempotency, normalized errors, capability checks, recurring charges, optional tax calculation, and verified provider events. It does not own carts, discounts, product prices, shipping, identity, or persistence.

## Install and register

Install the core plus only the adapters the application needs:

```powershell
dotnet add package AngryMonkey.CloudPayments
dotnet add package AngryMonkey.CloudPayments.MyFatoorah
```

```csharp
builder.Services.AddCloudPayments(options =>
{
    options.TaxCalculationEnabled = false;
});

builder.Services.AddMyFatoorahCloudPayments(options =>
{
    options.ApiToken = configuration["MyFatoorah:ApiToken"]!;
    options.WebhookSecret = configuration["MyFatoorah:WebhookSecret"]!;
    options.PaymentMethodId = 2;
});
```

Keep credentials in user secrets, environment variables, or managed secret storage.

## Supported providers

| Adapter | Fit | Core capabilities | Official sandbox and test resources |
| --- | --- | --- | --- |
| `Stripe` | Global | PaymentIntents, authorize, capture, cancel, refund, saved references, recurring, signed webhooks | [Test mode and cards](https://docs.stripe.com/testing) |
| `PayPal` | Global | Orders, approval redirects, capture, authorize/void, refund, billing references, verified webhooks | [Sandbox](https://developer.paypal.com/tools/sandbox/) · [card testing](https://developer.paypal.com/sandbox-testing/card-testing) |
| `Adyen` | Enterprise/global | Checkout actions, stored references, recurring, capture, cancel, refund, HMAC webhooks | [Test cards and credentials](https://docs.adyen.com/development-resources/test-cards-and-credentials) |
| `MyFatoorah` | MENA | Hosted create/authorize, invoice lookup, capture/release, refund, signed events | [Sandbox setup](https://docs.myfatoorah.com/docs/get-started) · [test cards](https://docs.myfatoorah.com/docs/test-cards) |
| `SkipCash` | Qatar | QAR hosted checkout, HMAC signing, lookup, normalized status, signed callbacks | [Integration manual](https://skipcash.app/assets/doc/SkipCashIntegrationManual.pdf) · [merchant portal](https://merchantportal.skipcash.app/) |
| `Tap` | MENA | Charges, authorize, capture, void, partial/full refund, saved sources, hash validation | [Get started](https://developers.tap.company/docs/get-started) · [testing cards](https://developers.tap.company/reference/testing-cards) |
| `PayTabs` | MENA | Hosted sale/authorization, capture, void, partial/full refund, query, signed callbacks | [Hosted API](https://support.paytabs.com/en/support/solutions/articles/60000992876-3-2-1-hosted-payment-page-apis-initiating-the-payment) · [test cards](https://support.paytabs.com/en/support/solutions/articles/60000712315-what-are-the-test-cards-available-to-perform-payments-) |

## Execute a payment

```csharp
IPaymentService payments = services.GetRequiredService<IPaymentService>();
PaymentResult result = await payments.CreateAsync(new PaymentRequest
{
    Provider = "MyFatoorah",
    Amount = new Money("KWD", 25m),
    IdempotencyKey = $"order-{orderId}",
    Description = "Order payment"
});

if (result.Payment?.NextAction is { } action)
    navigation.NavigateTo(action.Url.ToString(), forceLoad: true);
```

Hosted gateways commonly return `PaymentStatuses.RequiresAction`. The application redirects to the provider; CloudPayments does not collect raw card details.

## Extension and test model

Implement `IPaymentProvider` for another gateway, declare its capabilities, normalize provider failures, and validate webhook authenticity before returning provider events. `ITaxCalculator` is optional and disabled by default. Core and provider tests use fakes and injected HTTP handlers; they never call a real gateway.

Explore every adapter in the [interactive demo](../CloudCommerce.Demo/README.md) or read the focused [provider reference](docs/providers.md). See also [CloudCommerce](../CloudCommerce/README.md).


## Interactive sandbox labs

CloudCommerce.Demo provides a dedicated page for every adapter at `/payments/{provider}`. After configuring provider-issued test credentials with .NET user-secrets, the lab invokes the real adapter test endpoint through `IPaymentService`, displays the normalized response, and links to the returned provider-hosted checkout. No credentials or raw card data are collected by the UI. See the [demo guide](../CloudCommerce.Demo/README.md) for routes and setup.

CloudPayments is part of [Angry Monkey Cloud](https://angrymonkeycloud.com). Development follows the shared [AI instructions](https://github.com/angrymonkeycloud/CloudDocs/blob/main/docs/ai/instructions.md).