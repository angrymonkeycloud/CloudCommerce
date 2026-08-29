# CloudPayments Stripe Provider

Stripe payment processing for `AngryMonkey.CloudPayments`, including PaymentIntents, authorization, capture, refund, recurring charges, and signed webhooks.

## Registration

```csharp
services.AddCloudPayments();
services.AddStripeCloudPayments(options =>
{
    options.SecretKey = configuration["Stripe:SecretKey"]!;
    options.WebhookSecret = configuration["Stripe:WebhookSecret"]!;
});
```

The core CloudPayments package remains provider neutral. Card data must be collected with Stripe-hosted client components and supplied as a payment-method reference.

## API version

Built on [Stripe.net](https://github.com/stripe/stripe-dotnet), which sends the API version it was
generated against on every request. There is no version setting to configure, and deliberately so:
the SDK's models only understand that one version, and an account whose Dashboard default differs
can no longer change what comes back. Moving to a newer Stripe API means upgrading the package.

`CustomerReference` on a payment request is Stripe's own customer id (`cus_…`). It is not a place
to put an application's user id — Stripe rejects the request for an id it doesn't know. Use
`Metadata` to carry your own identifiers; it round-trips, and is what a settled payment can be
matched back to your records with.
