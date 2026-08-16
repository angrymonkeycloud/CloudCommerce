using AngryMonkey.CloudLogistics;
using AngryMonkey.CloudLogistics.FedEx;

namespace Microsoft.Extensions.DependencyInjection;

public static class FedExCloudLogisticsServiceCollectionExtensions
{
    public static IServiceCollection AddFedExCloudLogistics(this IServiceCollection services, Action<FedExOptions> configure)
    {
        FedExOptions options = new();
        configure(options);
        services.AddSingleton(options);
        services.AddHttpClient<FedExShippingProvider>(client => client.BaseAddress = options.ApiBaseAddress);
        services.AddSingleton<IShippingProvider>(provider => provider.GetRequiredService<FedExShippingProvider>());
        return services;
    }
}
