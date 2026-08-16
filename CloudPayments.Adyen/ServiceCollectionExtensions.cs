using AngryMonkey.CloudPayments;
using AngryMonkey.CloudPayments.Adyen;

namespace Microsoft.Extensions.DependencyInjection;

public static class AdyenCloudPaymentsServiceCollectionExtensions
{
    public static IServiceCollection AddAdyenCloudPayments(this IServiceCollection services, Action<AdyenOptions> configure)
    {
        AdyenOptions options = new();
        configure(options);
        services.AddSingleton(options);
        services.AddHttpClient<AdyenPaymentProvider>(client => client.BaseAddress = options.ApiBaseAddress);
        services.AddSingleton<IPaymentProvider>(provider => provider.GetRequiredService<AdyenPaymentProvider>());
        return services;
    }
}
