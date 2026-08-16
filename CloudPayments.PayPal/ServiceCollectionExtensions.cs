using AngryMonkey.CloudPayments;
using AngryMonkey.CloudPayments.PayPal;

namespace Microsoft.Extensions.DependencyInjection;

public static class PayPalCloudPaymentsServiceCollectionExtensions
{
    public static IServiceCollection AddPayPalCloudPayments(this IServiceCollection services, Action<PayPalOptions> configure)
    {
        PayPalOptions options = new();
        configure(options);
        services.AddSingleton(options);
        services.AddHttpClient<PayPalPaymentProvider>(client => client.BaseAddress = options.ApiBaseAddress);
        services.AddSingleton<IPaymentProvider>(provider => provider.GetRequiredService<PayPalPaymentProvider>());
        return services;
    }
}
