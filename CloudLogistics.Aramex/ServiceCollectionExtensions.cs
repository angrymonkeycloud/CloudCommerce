using AngryMonkey.CloudLogistics;
using AngryMonkey.CloudLogistics.Aramex;

namespace Microsoft.Extensions.DependencyInjection;

public static class AramexCloudLogisticsServiceCollectionExtensions
{
    public static IServiceCollection AddAramexCloudLogistics(this IServiceCollection services, Action<AramexOptions> configure)
    {
        AramexOptions options = new();
        configure(options);
        services.AddSingleton(options);
        services.AddHttpClient<AramexShippingProvider>(client => client.BaseAddress = options.ApiBaseAddress);
        services.AddSingleton<IShippingProvider>(provider => provider.GetRequiredService<AramexShippingProvider>());
        return services;
    }
}
