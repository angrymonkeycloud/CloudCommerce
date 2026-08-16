# CloudPayments Adyen Provider

Adyen payment, modification, stored-method, recurring payment, and HMAC webhook support for `AngryMonkey.CloudPayments`.

## Registration

```csharp
services.AddAdyenCloudPayments(options =>
{
    options.ApiKey = configuration["Adyen:ApiKey"]!;
    options.MerchantAccount = configuration["Adyen:MerchantAccount"]!;
    options.HmacKey = configuration["Adyen:HmacKey"]!;
});
```

The default endpoint is Adyen's test checkout environment.
