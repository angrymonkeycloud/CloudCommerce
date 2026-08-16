using AngryMonkey.Cloud;
using AngryMonkey.CloudLogistics;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

public static class CloudLogisticsServiceCollectionExtensions
{
    public static IServiceCollection AddCloudLogistics(this IServiceCollection services)
    {
        services.TryAddSingleton<CloudGeographyClient>();
        services.TryAddSingleton<IInventoryStore, InMemoryInventoryStore>();
        services.TryAddScoped<IInventoryService, InventoryService>();
        services.TryAddSingleton<IShippingProviderRegistry, ShippingProviderRegistry>();
        services.TryAddSingleton<IGeographicAddressValidator, CloudGeographyAddressValidator>();
        return services;
    }

    public static IServiceCollection AddCloudShippingProvider<TProvider>(this IServiceCollection services) where TProvider : class, IShippingProvider
    {
        services.AddSingleton<IShippingProvider, TProvider>();
        return services;
    }
}
