using AngryMonkey.CloudPayments;
using AngryMonkey.CloudPayments.Stripe;

namespace Microsoft.Extensions.DependencyInjection;

public static class StripeCloudPaymentsServiceCollectionExtensions
{
    public static IServiceCollection AddStripeCloudPayments(this IServiceCollection services, Action<StripeOptions> configure)
    {
        StripeOptions options = new();
        configure(options);
        services.AddSingleton(options);
        services.AddHttpClient<StripePaymentProvider>(client => client.BaseAddress = options.ApiBaseAddress);
        services.AddSingleton<IPaymentProvider>(provider => provider.GetRequiredService<StripePaymentProvider>());
        return services;
    }
}
