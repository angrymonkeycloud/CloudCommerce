using AngryMonkey.CloudLogistics;
using AngryMonkey.CloudLogistics.FedEx;

namespace CloudLogistics.Providers.Tests;

public class FedExShippingProviderTests
{
    private static FedExOptions Options => new() { ClientId = "client", ClientSecret = "secret", AccountNumber = "123" };

    [Fact]
    public async Task GetRatesAsync_authenticates_and_maps_rate()
    {
        StubHttpMessageHandler handler = new((_, call) => call == 1
            ? StubHttpMessageHandler.Json("""{"access_token":"token","expires_in":3600}""")
            : StubHttpMessageHandler.Json("""{"output":{"rateReplyDetails":[{"serviceType":"FEDEX_INTERNATIONAL_PRIORITY","serviceName":"International Priority","ratedShipmentDetails":[{"totalNetCharge":{"currency":"USD","amount":55.25}}]}]}}"""));
        FedExShippingProvider provider = new(new HttpClient(handler) { BaseAddress = new("https://fedex.test/") }, Options);

        IReadOnlyList<ShippingRate> rates = await provider.GetRatesAsync(new(TestAddresses.Origin, TestAddresses.Destination, TestAddresses.Packages));

        Assert.Equal(2, handler.Calls);
        Assert.Equal("FEDEX_INTERNATIONAL_PRIORITY", Assert.Single(rates).ServiceCode);
        Assert.Equal(55.25m, rates[0].Price.Value);
    }

    [Fact]
    public async Task CreateShipmentAsync_maps_tracking_and_document()
    {
        StubHttpMessageHandler handler = new((_, call) => call == 1
            ? StubHttpMessageHandler.Json("""{"access_token":"token","expires_in":3600}""")
            : StubHttpMessageHandler.Json("""{"output":{"transactionShipments":[{"masterTrackingNumber":"FDX123","pieceResponses":[{"packageDocuments":[{"encodedLabel":"BASE64"}]}]}]}}"""));
        FedExShippingProvider provider = new(new HttpClient(handler) { BaseAddress = new("https://fedex.test/") }, Options);

        Shipment shipment = await provider.CreateShipmentAsync(new(TestAddresses.Origin, TestAddresses.Destination, TestAddresses.Packages, "FEDEX_INTERNATIONAL_PRIORITY"));

        Assert.Equal("FDX123", shipment.TrackingNumber);
        Assert.Equal("BASE64", Assert.Single(shipment.Documents!).Content);
    }
}
