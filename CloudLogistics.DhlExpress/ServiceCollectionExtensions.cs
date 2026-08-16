using AngryMonkey.CloudLogistics;
using AngryMonkey.CloudLogistics.DhlExpress;

namespace Microsoft.Extensions.DependencyInjection;

public static class DhlExpressCloudLogisticsServiceCollectionExtensions
{
    public static IServiceCollection AddDhlExpressCloudLogistics(this IServiceCollection services, Action<DhlExpressOptions> configure)
    {
        DhlExpressOptions options = new();
        configure(options);
        services.AddSingleton(options);
        services.AddHttpClient<DhlExpressShippingProvider>(client => client.BaseAddress = options.ApiBaseAddress);
        services.AddSingleton<IShippingProvider>(provider => provider.GetRequiredService<DhlExpressShippingProvider>());
        return services;
    }
}
