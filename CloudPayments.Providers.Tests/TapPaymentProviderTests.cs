using System.Security.Cryptography;
using System.Text;
using AngryMonkey.Cloud.Geography;
using AngryMonkey.CloudPayments;
using AngryMonkey.CloudPayments.Tap;

namespace CloudPayments.Providers.Tests;

public class TapPaymentProviderTests
{
    [Fact]
    public async Task CreateAsync_maps_hosted_redirect_and_authenticates()
    {
        StubHttpMessageHandler handler = new((_, _) => StubHttpMessageHandler.Json("""{"id":"chg_TS01","object":"charge","status":"INITIATED","amount":12.5,"currency":"KWD","transaction":{"url":"https://tap.test/redirect","created":"1698392719404"},"reference":{"order":"order-1"},"source":{"id":"src_all"}}"""));
        TapPaymentProvider provider = CreateProvider(handler);

        PaymentResult result = await provider.CreateAsync(new()
        {
            Provider = "Tap",
            Amount = new Money("KWD", 12.5m),
            IdempotencyKey = "order-1",
            Metadata = new() { ["email"] = "demo@example.com" }
        });

        Assert.Equal(PaymentStatuses.RequiresAction, result.Payment!.Status);
        Assert.Equal("https://tap.test/redirect", result.Payment.NextAction!.Url!.ToString());
        Assert.Equal("Bearer", handler.Requests[0].Headers.Authorization!.Scheme);
        Assert.Equal("sk_test", handler.Requests[0].Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task AuthorizeAsync_maps_authorized_status()
    {
        StubHttpMessageHandler handler = new((_, _) => StubHttpMessageHandler.Json("""{"id":"auth_TS01","object":"authorize","status":"AUTHORIZED","amount":25,"currency":"SAR","transaction":{"created":"1698392719404"}}"""));
        TapPaymentProvider provider = CreateProvider(handler);

        PaymentResult result = await provider.AuthorizeAsync(new() { Provider = "Tap", Amount = new Money("SAR", 25m), IdempotencyKey = "auth-1" });

        Assert.Equal(PaymentStatuses.Authorized, result.Payment!.Status);
        Assert.Equal("auth_TS01", result.Payment.Id);
    }

    [Fact]
    public async Task RefundAsync_maps_partial_refund_to_original_charge()
    {
        StubHttpMessageHandler handler = new((_, _) => StubHttpMessageHandler.Json("""{"id":"re_TS01","object":"refund","status":"PENDING","amount":5,"currency":"KWD","charge_id":"chg_TS01"}"""));
        TapPaymentProvider provider = CreateProvider(handler);

        PaymentResult result = await provider.RefundAsync(new("Tap", "chg_TS01", new Money("KWD", 5m), "refund-1", IsPartial: true));

        Assert.Equal("chg_TS01", result.Payment!.Id);
        Assert.Equal("re_TS01", result.Payment.ProviderReference);
        Assert.Equal(PaymentStatuses.PartiallyRefunded, result.Payment.Status);
    }

    [Fact]
    public async Task ValidateWebhookAsync_verifies_tap_hashstring()
    {
        const string secret = "sk_test";
        string body = """{"id":"chg_TS01","object":"charge","status":"CAPTURED","amount":1.0,"currency":"KWD","transaction":{"created":"1698392719404"},"reference":{"gateway":"gateway-1","payment":"payment-1"}}""";
        string signedValue = "x_idchg_TS01x_amount1.000x_currencyKWDx_gateway_referencegateway-1x_payment_referencepayment-1x_statusCAPTUREDx_created1698392719404";
        string signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(signedValue))).ToLowerInvariant();
        TapPaymentProvider provider = new(new HttpClient(new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json("{}"))) { BaseAddress = new("https://tap.test/") }, new TapOptions { SecretKey = secret });

        bool valid = await provider.ValidateWebhookAsync(new("Tap", new Dictionary<string, string> { ["hashstring"] = signature }, Encoding.UTF8.GetBytes(body)));
        bool invalid = await provider.ValidateWebhookAsync(new("Tap", new Dictionary<string, string> { ["hashstring"] = signature }, Encoding.UTF8.GetBytes(body.Replace("CAPTURED", "FAILED"))));

        Assert.True(valid);
        Assert.False(invalid);
    }

    private static TapPaymentProvider CreateProvider(StubHttpMessageHandler handler)
        => new(new HttpClient(handler) { BaseAddress = new("https://tap.test/") }, new TapOptions { SecretKey = "sk_test", MerchantId = "merchant-1" });
}