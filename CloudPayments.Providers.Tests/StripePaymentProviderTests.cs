using System.Security.Cryptography;
using System.Text;
using AngryMonkey.Cloud.Geography;
using AngryMonkey.CloudPayments;
using AngryMonkey.CloudPayments.Stripe;

namespace CloudPayments.Providers.Tests;

public class StripePaymentProviderTests
{
    [Fact]
    public async Task CreateAsync_maps_requires_action_and_client_secret()
    {
        StubHttpMessageHandler handler = new((_, _) => StubHttpMessageHandler.Json("""{"id":"pi_123","object":"payment_intent","amount":1250,"currency":"usd","status":"requires_action","client_secret":"secret_123","created":1700000000}"""));
        StripePaymentProvider provider = new(new HttpClient(handler) { BaseAddress = new("https://api.test/") }, new StripeOptions { SecretKey = "sk_test" });

        PaymentResult result = await provider.CreateAsync(new() { Provider = "Stripe", Amount = new Money("USD", 12.50m), IdempotencyKey = "order-1", PaymentMethod = new("Stripe", "pm_123") });

        Assert.True(result.IsSuccessful);
        Assert.Equal(PaymentStatuses.RequiresAction, result.Payment!.Status);
        Assert.Equal("secret_123", result.Payment.NextAction!.ClientSecret);
        Assert.Equal("order-1", handler.Requests[0].Headers.GetValues("Idempotency-Key").Single());
    }

    [Fact]
    public async Task RefundAsync_maps_partial_refund_to_original_payment()
    {
        StubHttpMessageHandler handler = new((_, _) => StubHttpMessageHandler.Json("""{"id":"re_123","object":"refund","amount":500,"currency":"usd","status":"succeeded"}"""));
        StripePaymentProvider provider = new(new HttpClient(handler) { BaseAddress = new("https://api.test/") }, new StripeOptions { SecretKey = "sk_test" });

        PaymentResult result = await provider.RefundAsync(new("Stripe", "pi_123", new Money("USD", 5m), "refund-1", IsPartial: true));

        Assert.Equal("pi_123", result.Payment!.Id);
        Assert.Equal("re_123", result.Payment.ProviderReference);
        Assert.Equal(PaymentStatuses.PartiallyRefunded, result.Payment.Status);
    }

    [Fact]
    public async Task ValidateWebhookAsync_accepts_current_valid_signature_and_rejects_tampering()
    {
        string body = """{"id":"evt_1"}""";
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string secret = "whsec_test";
        string signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}"))).ToLowerInvariant();
        StripePaymentProvider provider = new(new HttpClient(new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json("{}"))) { BaseAddress = new("https://api.test/") }, new StripeOptions { SecretKey = "sk_test", WebhookSecret = secret });

        bool valid = await provider.ValidateWebhookAsync(new("Stripe", new Dictionary<string, string> { ["Stripe-Signature"] = $"t={timestamp},v1={signature}" }, Encoding.UTF8.GetBytes(body)));
        bool invalid = await provider.ValidateWebhookAsync(new("Stripe", new Dictionary<string, string> { ["Stripe-Signature"] = $"t={timestamp},v1={signature}" }, Encoding.UTF8.GetBytes(body + "x")));

        Assert.True(valid);
        Assert.False(invalid);
    }
}
