using AngryMonkey.CloudLogistics;

namespace CloudLogistics.Providers.Tests;

internal static class TestAddresses
{
    public static LogisticsAddress Origin => new() { CountryCode = "LB", Locality = "Beirut", Line1 = "1 Origin Street", PostalCode = "1107", RecipientName = "Sender", PhoneNumber = "9611000000" };
    public static LogisticsAddress Destination => new() { CountryCode = "AE", Locality = "Dubai", Line1 = "2 Destination Street", PostalCode = "00000", RecipientName = "Receiver", PhoneNumber = "9714000000" };
    public static IReadOnlyList<ShippingPackage> Packages => [new(2m, 20m, 15m, 10m)];
}
