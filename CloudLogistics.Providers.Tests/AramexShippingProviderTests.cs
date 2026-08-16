using AngryMonkey.CloudLogistics;
using AngryMonkey.CloudLogistics.Aramex;

namespace CloudLogistics.Providers.Tests;

public class AramexShippingProviderTests
{
    private static AramexOptions Options => new() { UserName = "user", Password = "password", AccountNumber = "1", AccountPin = "2", AccountEntity = "BEY", AccountCountryCode = "LB" };

    [Fact]
    public async Task GetRatesAsync_maps_total_amount()
    {
        StubHttpMessageHandler handler = new((_, _) => StubHttpMessageHandler.Json("""{"HasErrors":false,"TotalAmount":{"CurrencyCode":"USD","Value":18.75}}"""));
        AramexShippingProvider provider = new(new HttpClient(handler) { BaseAddress = new("https://aramex.test/") }, Options);

        IReadOnlyList<ShippingRate> rates = await provider.GetRatesAsync(new(TestAddresses.Origin, TestAddresses.Destination, TestAddresses.Packages));

        Assert.Single(rates);
        Assert.Equal(18.75m, rates[0].Price.Value);
        Assert.Equal("Aramex", rates[0].Provider);
    }

    [Fact]
    public async Task CreateShipmentAsync_maps_tracking_and_label()
    {
        StubHttpMessageHandler handler = new((_, _) => StubHttpMessageHandler.Json("""{"HasErrors":false,"Shipments":[{"ID":"123456","ShipmentLabel":{"LabelURL":"https://aramex.test/label.pdf"}}]}"""));
        AramexShippingProvider provider = new(new HttpClient(handler) { BaseAddress = new("https://aramex.test/") }, Options);

        Shipment shipment = await provider.CreateShipmentAsync(new(TestAddresses.Origin, TestAddresses.Destination, TestAddresses.Packages, "PDX"));

        Assert.Equal("123456", shipment.TrackingNumber);
        Assert.Equal("https://aramex.test/label.pdf", shipment.LabelUrl!.ToString());
        Assert.Equal(ShipmentStatuses.LabelCreated, shipment.Status);
    }
}
