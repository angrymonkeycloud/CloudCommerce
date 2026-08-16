using AngryMonkey.CloudPayments;
using AngryMonkey.CloudPayments.Tap;

namespace Microsoft.Extensions.DependencyInjection;

public static class TapCloudPaymentsServiceCollectionExtensions
{
    public static IServiceCollection AddTapCloudPayments(this IServiceCollection services, Action<TapOptions> configure)
    {
        TapOptions options = new();
        configure(options);
        services.AddSingleton(options);
        services.AddHttpClient<TapPaymentProvider>(client => client.BaseAddress = options.ApiBaseAddress);
        services.AddSingleton<IPaymentProvider>(provider => provider.GetRequiredService<TapPaymentProvider>());
        return services;
    }
}