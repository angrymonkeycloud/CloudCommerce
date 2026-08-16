using AngryMonkey.CloudCommerce.Components;

namespace Microsoft.Extensions.DependencyInjection;

public static class CloudCommerceComponentsServiceCollectionExtensions
{
    public static IServiceCollection AddCloudCommerceComponents(this IServiceCollection services, Action<CommerceComponentOptions>? configure = null)
    {
        CommerceComponentOptions options = new();
        configure?.Invoke(options);
        services.AddSingleton(options);
        return services;
    }
}
