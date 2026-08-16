using AngryMonkey.CloudLogin;

namespace AngryMonkey.CloudCommerce;

public static class CloudLoginCommerceAccountExtensions
{
    public static CommerceAccountReference ToCommerceAccount(this UserModel user) => new(UserId: user.ID);
    public static CommerceAccountReference ToCommerceAccount(this CloudLoginOrganization organization) => new(OrganizationId: organization.Id);
}
