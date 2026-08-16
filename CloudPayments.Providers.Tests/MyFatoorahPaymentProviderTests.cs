using System.Security.Cryptography;
using System.Text;
using AngryMonkey.Cloud.Geography;
using AngryMonkey.CloudPayments;
using AngryMonkey.CloudPayments.MyFatoorah;

namespace CloudPayments.Providers.Tests;

public class MyFatoorahPaymentProviderTests
{
    [Fact]
    public async Task CreateAsync_maps_invoice_redirect_and_bearer_token()
    {
        StubHttpMessageHandler handler = new((_, _) => StubHttpMessageHandler.Json("""{"IsSuccess":true,"Data":{"InvoiceId":927972,"PaymentURL":"https://demo.myfatoorah.com/pay/927972"}}"""));
        MyFatoorahPaymentProvider provider = CreateProvider(handler);

        PaymentResult result = await provider.CreateAsync(new()
        {
            Provider = "MyFatoorah",
            Amount = new Money("KWD", 10m),
            IdempotencyKey = "order-1",
            Metadata = new() { ["customer_name"] = "Demo Customer" }
        });

        Assert.Equal(PaymentStatuses.RequiresAction, result.Payment!.Status);
        Assert.Equal("927972", result.Payment.Id);
        Assert.Equal("https://demo.myfatoorah.com/pay/927972", result.Payment.NextAction!.Url!.ToString());
        Assert.Equal("Bearer", handler.Requests[0].Headers.Authorization!.Scheme);
        Assert.Equal("test-token", handler.Requests[0].Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task RefundAsync_returns_pending_provider_request()
    {
        StubHttpMessageHandler handler = new((_, _) => StubHttpMessageHandler.Json("""{"IsSuccess":true,"Data":{"RefundId":85342,"RefundReference":"2023000787"}}"""));
        MyFatoorahPaymentProvider provider = CreateProvider(handler);

        PaymentResult result = await provider.RefundAsync(new("MyFatoorah", "927972", new Money("KWD", 2.5m), "refund-1", IsPartial: true));

        Assert.Equal(PaymentStatuses.Pending, result.Payment!.Status);
        Assert.Equal("2023000787", result.Payment.ProviderReference);
        Assert.Equal("partial_refund_requested", result.Payment.Metadata["operation"]);
    }

    [Fact]
    public async Task GetAsync_maps_authorized_invoice()
    {
        StubHttpMessageHandler handler = new((_, _) => StubHttpMessageHandler.Json("""{"IsSuccess":true,"Data":{"Invoice":{"Id":"6409988","Status":"PENDING","CreationDate":"2026-01-04T08:14:49Z"},"Transaction":{"Status":"AUTHORIZE","PaymentId":"07076409988323998875"},"Amount":{"DisplayCurrency":"KWD","ValueInDisplayCurrency":"20"}}}"""));
        MyFatoorahPaymentProvider provider = CreateProvider(handler);

        Payment? payment = await provider.GetAsync("6409988");

        Assert.Equal(PaymentStatuses.Authorized, payment!.Status);
        Assert.Equal("KWD", payment.Amount.Currency);
        Assert.Equal("07076409988323998875", payment.ProviderReference);
    }

    [Fact]
    public async Task ValidateWebhookAsync_accepts_official_v2_signature_and_rejects_tampering()
    {
        const string secret = "webhook-secret";
        string body = """{"Event":{"Code":1,"Name":"PAYMENT_STATUS_CHANGED","CreationDate":"2026-01-04T08:15:00Z","Reference":"WH-1"},"Data":{"Invoice":{"Id":"6409988","Status":"PAID","ExternalIdentifier":"order-1"},"Transaction":{"Status":"SUCCESS","PaymentId":"07076409988323998875"}}}""";
        string signedValue = "Invoice.Id=6409988,Invoice.Status=PAID,Transaction.Status=SUCCESS,Transaction.PaymentId=07076409988323998875,Invoice.ExternalIdentifier=order-1";
        string signature = Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(signedValue)));
        MyFatoorahPaymentProvider provider = new(new HttpClient(new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json("{}"))) { BaseAddress = new("https://myfatoorah.test/") }, new MyFatoorahOptions { ApiToken = "test-token", WebhookSecret = secret });

        bool valid = await provider.ValidateWebhookAsync(new("MyFatoorah", new Dictionary<string, string> { ["MyFatoorah-Signature"] = signature }, Encoding.UTF8.GetBytes(body)));
        bool invalid = await provider.ValidateWebhookAsync(new("MyFatoorah", new Dictionary<string, string> { ["MyFatoorah-Signature"] = signature }, Encoding.UTF8.GetBytes(body.Replace("PAID", "PENDING"))));

        Assert.True(valid);
        Assert.False(invalid);
    }

    private static MyFatoorahPaymentProvider CreateProvider(StubHttpMessageHandler handler)
        => new(new HttpClient(handler) { BaseAddress = new("https://myfatoorah.test/") }, new MyFatoorahOptions { ApiToken = "test-token" });
}