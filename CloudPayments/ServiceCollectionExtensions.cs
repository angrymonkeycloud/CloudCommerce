using AngryMonkey.CloudPayments;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

public static class CloudPaymentsServiceCollectionExtensions
{
    public static IServiceCollection AddCloudPayments(this IServiceCollection services, Action<CloudPaymentsOptions>? configure = null)
    {
        CloudPaymentsOptions options = new();
        configure?.Invoke(options);

        services.TryAddSingleton(options);
        services.TryAddSingleton<IPaymentStore, InMemoryPaymentStore>();
        services.TryAddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
        services.TryAddSingleton<IPaymentProviderRegistry, PaymentProviderRegistry>();
        services.TryAddScoped<IPaymentService, PaymentService>();
        return services;
    }

    public static IServiceCollection AddCloudPaymentProvider<TProvider>(this IServiceCollection services) where TProvider : class, IPaymentProvider
    {
        services.AddSingleton<IPaymentProvider, TProvider>();
        return services;
    }
}
