using AngryMonkey.CloudLogistics;
using AngryMonkey.CloudLogistics.DhlExpress;

namespace CloudLogistics.Providers.Tests;

public class DhlExpressShippingProviderTests
{
    private static DhlExpressOptions Options => new() { UserName = "user", Password = "password", AccountNumber = "123" };

    [Fact]
    public async Task GetRatesAsync_maps_products_and_transit_time()
    {
        StubHttpMessageHandler handler = new((_, _) => StubHttpMessageHandler.Json("""{"products":[{"productCode":"P","productName":"Express Worldwide","totalPrice":[{"priceCurrency":"USD","price":42.5}],"deliveryCapabilities":{"totalTransitDays":2}}]}"""));
        DhlExpressShippingProvider provider = new(new HttpClient(handler) { BaseAddress = new("https://dhl.test/") }, Options);

        IReadOnlyList<ShippingRate> rates = await provider.GetRatesAsync(new(TestAddresses.Origin, TestAddresses.Destination, TestAddresses.Packages));

        Assert.Equal("P", Assert.Single(rates).ServiceCode);
        Assert.Equal(TimeSpan.FromDays(2), rates[0].EstimatedTransitTime);
        Assert.Equal("Basic", handler.Requests[0].Authorization);
    }

    [Fact]
    public async Task CreateShipmentAsync_preserves_base64_label_document()
    {
        StubHttpMessageHandler handler = new((_, _) => StubHttpMessageHandler.Json("""{"shipmentTrackingNumber":"DHL123","documents":[{"typeCode":"label","imageFormat":"PDF","content":"BASE64"}]}"""));
        DhlExpressShippingProvider provider = new(new HttpClient(handler) { BaseAddress = new("https://dhl.test/") }, Options);

        Shipment shipment = await provider.CreateShipmentAsync(new(TestAddresses.Origin, TestAddresses.Destination, TestAddresses.Packages, "P"));

        Assert.Equal("DHL123", shipment.TrackingNumber);
        Assert.Equal("BASE64", Assert.Single(shipment.Documents!).Content);
    }
}
