using AngryMonkey.CloudPayments;
using AngryMonkey.CloudPayments.PayTabs;

namespace Microsoft.Extensions.DependencyInjection;

public static class PayTabsCloudPaymentsServiceCollectionExtensions
{
    public static IServiceCollection AddPayTabsCloudPayments(this IServiceCollection services, Action<PayTabsOptions> configure)
    {
        PayTabsOptions options = new();
        configure(options);
        services.AddSingleton(options);
        services.AddHttpClient<PayTabsPaymentProvider>(client => client.BaseAddress = options.ApiBaseAddress);
        services.AddSingleton<IPaymentProvider>(provider => provider.GetRequiredService<PayTabsPaymentProvider>());
        return services;
    }
}