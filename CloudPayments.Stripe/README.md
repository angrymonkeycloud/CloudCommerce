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
