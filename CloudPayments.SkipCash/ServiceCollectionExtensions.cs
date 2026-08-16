using AngryMonkey.CloudPayments;
using AngryMonkey.CloudPayments.SkipCash;

namespace Microsoft.Extensions.DependencyInjection;

public static class SkipCashCloudPaymentsServiceCollectionExtensions
{
    public static IServiceCollection AddSkipCashCloudPayments(this IServiceCollection services, Action<SkipCashOptions> configure)
    {
        SkipCashOptions options = new();
        configure(options);
        services.AddSingleton(options);
        services.AddHttpClient<SkipCashPaymentProvider>(client => client.BaseAddress = options.ApiBaseAddress);
        services.AddSingleton<IPaymentProvider>(provider => provider.GetRequiredService<SkipCashPaymentProvider>());
        return services;
    }
}