using AngryMonkey.CloudCommerce;
using AngryMonkey.CloudLogin;

namespace CloudCommerce.Tests;

public class CloudLoginCommerceAccountExtensionsTests
{
    [Fact]
    public void ToCommerceAccount_maps_user_identifier()
    {
        Guid userId = Guid.NewGuid();
        UserModel user = new() { ID = userId };

        CommerceAccountReference account = user.ToCommerceAccount();

        Assert.Equal(userId, account.UserId);
        Assert.Null(account.OrganizationId);
    }

    [Fact]
    public void ToCommerceOrganizationAccount_maps_organization_identifier()
    {
        Guid organizationId = Guid.NewGuid();

        CommerceAccountReference account = CloudLoginCommerceAccountExtensions.ToCommerceOrganizationAccount(organizationId);

        Assert.Equal(organizationId, account.OrganizationId);
        Assert.Null(account.UserId);
    }
}