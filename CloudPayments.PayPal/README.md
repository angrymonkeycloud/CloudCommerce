# CloudPayments PayPal Provider

PayPal Orders v2, authorization, capture, void, refund, saved reference, recurring charge, and webhook support for `AngryMonkey.CloudPayments`.

## Registration

```csharp
services.AddPayPalCloudPayments(options =>
{
    options.ClientId = configuration["PayPal:ClientId"]!;
    options.ClientSecret = configuration["PayPal:ClientSecret"]!;
    options.WebhookId = configuration["PayPal:WebhookId"]!;
});
```

The default API address is PayPal sandbox. Set the production address explicitly when deploying.
