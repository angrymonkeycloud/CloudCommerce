using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AngryMonkey.Cloud.Geography;
using AngryMonkey.CloudPayments;
using AngryMonkey.CloudPayments.SkipCash;

namespace CloudPayments.Providers.Tests;

public class SkipCashPaymentProviderTests
{
    [Fact]
    public async Task CreateAsync_signs_request_and_maps_hosted_redirect()
    {
        string? signature = null;
        string? requestBody = null;
        StubHttpMessageHandler handler = new((request, _) =>
        {
            signature = request.Headers.GetValues("Authorization").Single();
            requestBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return StubHttpMessageHandler.Json("""{"resultObj":{"id":"770bd18b-a505-468b-8de1-ac7f9555dc79","statusId":0,"created":"2026-08-16T10:00:00Z","payUrl":"https://skipcash.test/pay/770bd18b-a505-468b-8de1-ac7f9555dc79","amount":19.000,"currency":"QAR","transactionId":"order-1"}}""");
        });
        SkipCashPaymentProvider provider = CreateProvider(handler);

        PaymentResult result = await provider.CreateAsync(CreateRequest());

        Assert.Equal(PaymentStatuses.RequiresAction, result.Payment!.Status);
        Assert.Equal("https://skipcash.test/pay/770bd18b-a505-468b-8de1-ac7f9555dc79", result.Payment.NextAction!.Url!.ToString());
        Assert.False(string.IsNullOrWhiteSpace(signature));
        Assert.Contains(@"""keyId"":""key-id""", requestBody);
    }

    [Fact]
    public async Task CreateAsync_rejects_non_qar_without_calling_provider()
    {
        StubHttpMessageHandler handler = new((_, _) => StubHttpMessageHandler.Json("{}"));
        SkipCashPaymentProvider provider = CreateProvider(handler);
        PaymentRequest request = CreateRequest();
        request = new()
        {
            Provider = request.Provider,
            Amount = new Money("USD", 19m),
            IdempotencyKey = request.IdempotencyKey,
            Metadata = request.Metadata
        };

        PaymentResult result = await provider.CreateAsync(request);

        Assert.Equal(PaymentErrorTypes.InvalidRequest, result.Error!.Type);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task GetAsync_uses_client_id_and_maps_paid_status()
    {
        string? authorization = null;
        StubHttpMessageHandler handler = new((request, _) =>
        {
            authorization = request.Headers.GetValues("Authorization").Single();
            return StubHttpMessageHandler.Json("""{"resultObj":{"id":"payment-1","statusId":2,"amount":25,"currency":"QAR","transactionId":"order-1","visaId":"visa-1"}}""");
        });
        SkipCashPaymentProvider provider = CreateProvider(handler);

        Payment? payment = await provider.GetAsync("payment-1");

        Assert.Equal("client-id", authorization);
        Assert.Equal(PaymentStatuses.Captured, payment!.Status);
        Assert.Equal("visa-1", payment.ProviderReference);
    }

    [Fact]
    public async Task ValidateWebhookAsync_verifies_ordered_hmac()
    {
        const string secret = "webhook-secret";
        string body = """{"paymentId":"payment-1","amount":11.00,"statusId":2,"transactionId":"order-1","custom1":null,"visaId":"visa-1"}""";
        string signedValue = "PaymentId=payment-1,Amount=11.00,StatusId=2,TransactionId=order-1,VisaId=visa-1";
        string signature = Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(signedValue)));
        SkipCashPaymentProvider provider = new(new HttpClient(new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json("{}"))) { BaseAddress = new("https://skipcash.test/") }, new SkipCashOptions { ClientId = "client-id", KeyId = "key-id", KeySecret = "key-secret", WebhookKey = secret });

        bool valid = await provider.ValidateWebhookAsync(new("SkipCash", new Dictionary<string, string> { ["Authorization"] = signature }, Encoding.UTF8.GetBytes(body)));
        bool invalid = await provider.ValidateWebhookAsync(new("SkipCash", new Dictionary<string, string> { ["Authorization"] = signature }, Encoding.UTF8.GetBytes(body.Replace(@"""statusId"":2", @"""statusId"":4"))));

        Assert.True(valid);
        Assert.False(invalid);
    }

    private static PaymentRequest CreateRequest() => new()
    {
        Provider = "SkipCash",
        Amount = new Money("QAR", 19m),
        IdempotencyKey = "order-1",
        Metadata = new()
        {
            ["first_name"] = "Demo",
            ["last_name"] = "Customer",
            ["phone"] = "97450000000",
            ["email"] = "demo@example.com",
            ["street"] = "West Bay",
            ["city"] = "Doha",
            ["state"] = "DA",
            ["country"] = "QA",
            ["postal_code"] = "00000"
        }
    };

    private static SkipCashPaymentProvider CreateProvider(StubHttpMessageHandler handler)
        => new(new HttpClient(handler) { BaseAddress = new("https://skipcash.test/") }, new SkipCashOptions { ClientId = "client-id", KeyId = "key-id", KeySecret = "key-secret" });
}