using AngryMonkey.Cloud;
using AngryMonkey.CloudLogistics;

namespace CloudLogistics.Tests;

public class GeographicAddressValidatorTests
{
    [Fact]
    public void Validate_known_country_and_subdivision_uses_cloud_geography()
    {
        CloudGeographyAddressValidator validator = new(new CloudGeographyClient());
        LogisticsAddress address = new() { CountryCode = "US", SubdivisionCode = "CA", Locality = "Los Angeles", Line1 = "1 Main Street" };

        GeographyValidationResult result = validator.Validate(address);

        Assert.True(result.IsValid, result.Error);
    }
}
