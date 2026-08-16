using AngryMonkey.CloudCommerce;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

public static class CloudCommerceServiceCollectionExtensions
{
    public static IServiceCollection AddCloudCommerce(this IServiceCollection services)
    {
        services.TryAddSingleton<ICartStore, InMemoryCartStore>();
        services.TryAddSingleton<IOrderStore, InMemoryOrderStore>();
        services.TryAddSingleton<ICheckoutIdempotencyStore, InMemoryCheckoutIdempotencyStore>();
        services.TryAddScoped<ICommerceService, CommerceService>();
        return services;
    }

    public static IServiceCollection AddCloudCommercePayments(this IServiceCollection services)
    {
        services.TryAddScoped<ICommercePaymentGateway, CloudPaymentsCommerceGateway>();
        return services;
    }

    public static IServiceCollection AddCloudCommerceLogistics(this IServiceCollection services)
    {
        services.TryAddScoped<ICommerceLogisticsGateway, CloudLogisticsCommerceGateway>();
        return services;
    }

    public static IServiceCollection AddCloudCommerceBooking(this IServiceCollection services)
    {
        services.TryAddScoped<ICommerceBookingGateway, CloudBookingCommerceGateway>();
        return services;
    }
}
