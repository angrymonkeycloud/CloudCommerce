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

    /// <summary>
    /// Every request states the API version it expects. Left unstated, Stripe answers in whatever
    /// version an account's Dashboard happens to default to - which is how a test account still on
    /// a pre-2019 default once returned the legacy "requires_source" for a created intent, a status
    /// the mapping didn't know, so the client secret never reached the browser. The version is the
    /// SDK's own: its models only deserialise what it was generated against, so the two cannot
    /// drift apart.
    /// </summary>
    [Fact]
    public async Task CreateAsync_pins_the_sdk_api_version_rather_than_the_account_default()
    {
        StubHttpMessageHandler handler = new((_, _) => StubHttpMessageHandler.Json("""{"id":"pi_789","object":"payment_intent","amount":100,"currency":"usd","status":"requires_payment_method","client_secret":"secret_789","created":1700000000}"""));
        StripePaymentProvider provider = new(new HttpClient(handler) { BaseAddress = new("https://api.test/") }, new StripeOptions { SecretKey = "sk_test" });

        await provider.CreateAsync(new() { Provider = "Stripe", Amount = new Money("USD", 1m), IdempotencyKey = "order-3" });

        Assert.Equal(global::Stripe.StripeConfiguration.ApiVersion, handler.Requests[0].Headers.GetValues("Stripe-Version").Single());
    }

    /// <summary>
    /// Metadata is the only thread tying a Stripe payment back to the order it was raised for:
    /// the customer leaves for Stripe's hosted card form and returns with nothing but an intent
    /// id. Dropping it on the way back out left the settlement step unable to find the order, so
    /// a genuinely paid intent never marked anything paid.
    /// </summary>
    [Fact]
    public async Task GetAsync_returns_the_metadata_the_payment_was_created_with()
    {
        StubHttpMessageHandler handler = new((_, _) => StubHttpMessageHandler.Json("""{"id":"pi_meta","object":"payment_intent","amount":2500,"currency":"usd","status":"succeeded","created":1700000000,"metadata":{"orderId":"11111111-1111-1111-1111-111111111111","linkId":"abc123"}}"""));
        StripePaymentProvider provider = new(new HttpClient(handler) { BaseAddress = new("https://api.test/") }, new StripeOptions { SecretKey = "sk_test" });

        Payment? payment = await provider.GetAsync("pi_meta");

        Assert.Equal(PaymentStatuses.Captured, payment!.Status);
        Assert.Equal("abc123", payment.Metadata["linkId"]);
        Assert.Equal("11111111-1111-1111-1111-111111111111", payment.Metadata["orderId"]);
    }

    /// <summary>
    /// The ordinary card-payment flow: create an intent with no payment_method attached, hand
    /// its client_secret to Stripe.js so the customer enters card details in the browser. Stripe
    /// reports this intent as "requires_payment_method", not "requires_action" - a status this
    /// once treated as "nothing to do", leaving every such payment with no way to continue.
    /// </summary>
    [Fact]
    public async Task CreateAsync_surfaces_the_client_secret_for_a_freshly_created_intent()
    {
        StubHttpMessageHandler handler = new((_, _) => StubHttpMessageHandler.Json("""{"id":"pi_456","object":"payment_intent","amount":10000,"currency":"usd","status":"requires_payment_method","client_secret":"secret_456","created":1700000000}"""));
        StripePaymentProvider provider = new(new HttpClient(handler) { BaseAddress = new("https://api.test/") }, new StripeOptions { SecretKey = "sk_test" });

        PaymentResult result = await provider.CreateAsync(new() { Provider = "Stripe", Amount = new Money("USD", 100m), IdempotencyKey = "order-2" });

        Assert.True(result.IsSuccessful);
        Assert.Equal(PaymentStatuses.RequiresAction, result.Payment!.Status);
        Assert.Equal(PaymentActionTypes.ClientSecret, result.Payment.NextAction!.Type);
        Assert.Equal("secret_456", result.Payment.NextAction.ClientSecret);
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
