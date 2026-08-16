using System.Security.Cryptography;
using System.Text;
using AngryMonkey.Cloud.Geography;
using AngryMonkey.CloudPayments;
using AngryMonkey.CloudPayments.Adyen;

namespace CloudPayments.Providers.Tests;

public class AdyenPaymentProviderTests
{
    [Fact]
    public async Task CreateAsync_maps_redirect_action()
    {
        StubHttpMessageHandler handler = new((_, _) => StubHttpMessageHandler.Json("""{"pspReference":"PSP-1","resultCode":"RedirectShopper","amount":{"currency":"USD","value":1000},"action":{"type":"redirect","url":"https://adyen.test/action"}}"""));
        AdyenPaymentProvider provider = new(new HttpClient(handler) { BaseAddress = new("https://adyen.test/") }, new AdyenOptions { ApiKey = "key", MerchantAccount = "merchant" });

        PaymentResult result = await provider.CreateAsync(new() { Provider = "Adyen", Amount = new Money("USD", 10m), IdempotencyKey = "order-1" });

        Assert.Equal(PaymentStatuses.RequiresAction, result.Payment!.Status);
        Assert.Equal("https://adyen.test/action", result.Payment.NextAction!.Url!.ToString());
        Assert.Equal("key", handler.Requests[0].Headers.GetValues("X-API-Key").Single());
    }

    [Fact]
    public async Task ValidateWebhookAsync_verifies_adyen_hmac()
    {
        string keyHex = Convert.ToHexString(Encoding.UTF8.GetBytes("01234567890123456789012345678901"));
        string signingPayload = "PSP-1::merchant:order-1:1000:USD:AUTHORISATION:true";
        string signature = Convert.ToBase64String(HMACSHA256.HashData(Convert.FromHexString(keyHex), Encoding.UTF8.GetBytes(signingPayload)));
        string body = System.Text.Json.JsonSerializer.Serialize(new
        {
            notificationItems = new[]
            {
                new
                {
                    NotificationRequestItem = new
                    {
                        pspReference = "PSP-1",
                        originalReference = string.Empty,
                        merchantAccountCode = "merchant",
                        merchantReference = "order-1",
                        amount = new { value = 1000, currency = "USD" },
                        eventCode = "AUTHORISATION",
                        success = "true",
                        additionalData = new { hmacSignature = signature }
                    }
                }
            }
        });
        AdyenPaymentProvider provider = new(new HttpClient(new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json("{}"))) { BaseAddress = new("https://adyen.test/") }, new AdyenOptions { ApiKey = "key", MerchantAccount = "merchant", HmacKey = keyHex });

        bool valid = await provider.ValidateWebhookAsync(new("Adyen", new Dictionary<string, string>(), Encoding.UTF8.GetBytes(body)));

        Assert.True(valid);
    }
}
