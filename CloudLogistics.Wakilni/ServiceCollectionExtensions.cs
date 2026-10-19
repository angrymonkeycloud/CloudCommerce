using AngryMonkey.CloudLogistics;
using AngryMonkey.CloudLogistics.Wakilni;

namespace Microsoft.Extensions.DependencyInjection;

public static class WakilniCloudLogisticsServiceCollectionExtensions
{
    public static IServiceCollection AddWakilniCloudLogistics(this IServiceCollection services, Action<WakilniOptions> configure)
    {
        WakilniOptions options = new();
        configure(options);
        services.AddSingleton(options);
        services.AddHttpClient(nameof(WakilniShippingProvider), client =>
        {
            client.BaseAddress = options.ApiBaseAddress;
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddSingleton(provider => new WakilniShippingProvider(provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(WakilniShippingProvider)), options));
        services.AddSingleton<IShippingProvider>(provider => provider.GetRequiredService<WakilniShippingProvider>());
        return services;
    }
}
