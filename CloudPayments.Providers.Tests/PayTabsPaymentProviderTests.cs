using System.Security.Cryptography;
using System.Text;
using AngryMonkey.Cloud.Geography;
using AngryMonkey.CloudPayments;
using AngryMonkey.CloudPayments.PayTabs;

namespace CloudPayments.Providers.Tests;

public class PayTabsPaymentProviderTests
{
    [Fact]
    public async Task CreateAsync_maps_hosted_page_and_server_key()
    {
        string? authorization = null;
        StubHttpMessageHandler handler = new((request, _) =>
        {
            authorization = request.Headers.GetValues("authorization").Single();
            return StubHttpMessageHandler.Json("""{"tran_ref":"TST221","tran_type":"Sale","cart_id":"order-1","cart_currency":"AED","cart_amount":"100.00","redirect_url":"https://secure.paytabs.com/payment/page/TST221"}""");
        });
        PayTabsPaymentProvider provider = CreateProvider(handler);

        PaymentResult result = await provider.CreateAsync(new() { Provider = "PayTabs", Amount = new Money("AED", 100m), IdempotencyKey = "order-1" });

        Assert.Equal("server-key", authorization);
        Assert.Equal(PaymentStatuses.RequiresAction, result.Payment!.Status);
        Assert.Equal("https://secure.paytabs.com/payment/page/TST221", result.Payment.NextAction!.Url!.ToString());
    }

    [Fact]
    public async Task GetAsync_maps_successful_auth()
    {
        StubHttpMessageHandler handler = new((_, _) => StubHttpMessageHandler.Json("""{"tran_ref":"TST222","tran_type":"Auth","cart_id":"order-2","cart_currency":"SAR","cart_amount":"30.00","payment_result":{"response_status":"A","response_message":"Authorised"}}"""));
        PayTabsPaymentProvider provider = CreateProvider(handler);

        Payment? payment = await provider.GetAsync("TST222");

        Assert.Equal(PaymentStatuses.Authorized, payment!.Status);
        Assert.Equal("SAR", payment.Amount.Currency);
    }

    [Fact]
    public async Task CaptureAsync_queries_missing_amount_then_captures()
    {
        StubHttpMessageHandler handler = new((_, call) => call == 1
            ? StubHttpMessageHandler.Json("""{"tran_ref":"TST222","tran_type":"Auth","cart_currency":"SAR","cart_amount":"30.00","payment_result":{"response_status":"A"}}""")
            : StubHttpMessageHandler.Json("""{"tran_ref":"TST223","previous_tran_ref":"TST222","tran_type":"Capture","cart_currency":"SAR","cart_amount":"30.00","payment_result":{"response_status":"A"}}"""));
        PayTabsPaymentProvider provider = CreateProvider(handler);

        PaymentResult result = await provider.CaptureAsync(new("PayTabs", "TST222", IdempotencyKey: "capture-1"));

        Assert.Equal(2, handler.Calls);
        Assert.Equal("TST222", result.Payment!.Id);
        Assert.Equal("TST223", result.Payment.ProviderReference);
        Assert.Equal(PaymentStatuses.Captured, result.Payment.Status);
    }

    [Fact]
    public async Task ValidateWebhookAsync_verifies_raw_body_signature()
    {
        const string secret = "server-key";
        string body = """{"tran_ref":"TST224","tran_type":"Sale","cart_id":"order-4","payment_result":{"response_status":"A","response_message":"Authorised"}}""";
        string signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        PayTabsPaymentProvider provider = CreateProvider(new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json("{}")));

        bool valid = await provider.ValidateWebhookAsync(new("PayTabs", new Dictionary<string, string> { ["Signature"] = signature }, Encoding.UTF8.GetBytes(body)));
        bool invalid = await provider.ValidateWebhookAsync(new("PayTabs", new Dictionary<string, string> { ["Signature"] = signature }, Encoding.UTF8.GetBytes(body + " ")));

        Assert.True(valid);
        Assert.False(invalid);
    }

    private static PayTabsPaymentProvider CreateProvider(StubHttpMessageHandler handler)
        => new(new HttpClient(handler) { BaseAddress = new("https://paytabs.test/") }, new PayTabsOptions { ProfileId = 987654, ServerKey = "server-key" });
}