using AngryMonkey.Cloud.Geography;
using AngryMonkey.CloudPayments;
using AngryMonkey.CloudPayments.PayPal;

namespace CloudPayments.Providers.Tests;

public class PayPalPaymentProviderTests
{
    [Fact]
    public async Task CreateAsync_authenticates_and_returns_approval_redirect()
    {
        StubHttpMessageHandler handler = new((request, call) => call == 1
            ? StubHttpMessageHandler.Json("""{"access_token":"token","expires_in":3600}""")
            : StubHttpMessageHandler.Json("""{"id":"ORDER-1","status":"CREATED","purchase_units":[{"amount":{"currency_code":"USD","value":"25.00"}}],"links":[{"rel":"approve","href":"https://paypal.test/approve/ORDER-1"}]}"""));
        PayPalPaymentProvider provider = new(new HttpClient(handler) { BaseAddress = new("https://paypal.test/") }, new PayPalOptions { ClientId = "client", ClientSecret = "secret" });

        PaymentResult result = await provider.CreateAsync(new() { Provider = "PayPal", Amount = new Money("USD", 25m), IdempotencyKey = "checkout-1" });

        Assert.Equal(2, handler.Calls);
        Assert.Equal(PaymentStatuses.RequiresAction, result.Payment!.Status);
        Assert.Equal("https://paypal.test/approve/ORDER-1", result.Payment.NextAction!.Url!.ToString());
    }

    [Fact]
    public async Task ValidateWebhookAsync_returns_verification_result()
    {
        StubHttpMessageHandler handler = new((_, call) => call == 1
            ? StubHttpMessageHandler.Json("""{"access_token":"token","expires_in":3600}""")
            : StubHttpMessageHandler.Json("""{"verification_status":"SUCCESS"}"""));
        PayPalPaymentProvider provider = new(new HttpClient(handler) { BaseAddress = new("https://paypal.test/") }, new PayPalOptions { ClientId = "client", ClientSecret = "secret", WebhookId = "WH-1" });
        Dictionary<string, string> headers = new()
        {
            ["PAYPAL-AUTH-ALGO"] = "SHA256withRSA",
            ["PAYPAL-CERT-URL"] = "https://paypal.test/cert",
            ["PAYPAL-TRANSMISSION-ID"] = "tx",
            ["PAYPAL-TRANSMISSION-SIG"] = "sig",
            ["PAYPAL-TRANSMISSION-TIME"] = "2026-08-16T00:00:00Z"
        };

        bool valid = await provider.ValidateWebhookAsync(new("PayPal", headers, System.Text.Encoding.UTF8.GetBytes("""{"id":"WH-EVENT","event_type":"PAYMENT.CAPTURE.COMPLETED","resource":{}}""")));

        Assert.True(valid);
    }
}
