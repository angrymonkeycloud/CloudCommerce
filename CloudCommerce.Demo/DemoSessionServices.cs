using AngryMonkey.CloudBooking;
using AngryMonkey.CloudLogistics;
using AngryMonkey.CloudPayments;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AngryMonkey.CloudCommerce.Demo;

public static class DemoSessionServices
{
    public static IServiceCollection AddDemoSessionStores(this IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Scoped<ICartStore, InMemoryCartStore>());
        services.Replace(ServiceDescriptor.Scoped<IOrderStore, InMemoryOrderStore>());
        services.Replace(ServiceDescriptor.Scoped<ICheckoutIdempotencyStore, InMemoryCheckoutIdempotencyStore>());
        services.Replace(ServiceDescriptor.Scoped<IBookingStore, InMemoryBookingStore>());
        services.Replace(ServiceDescriptor.Scoped<IInventoryStore, InMemoryInventoryStore>());
        services.Replace(ServiceDescriptor.Scoped<IPaymentStore, InMemoryPaymentStore>());
        services.Replace(ServiceDescriptor.Scoped<IIdempotencyStore, InMemoryIdempotencyStore>());
        services.Replace(ServiceDescriptor.Scoped<IPaymentProviderRegistry, PaymentProviderRegistry>());
        services.Replace(ServiceDescriptor.Scoped<IShippingProviderRegistry, ShippingProviderRegistry>());
        services.AddScoped<IPaymentProvider, DemoPaymentProvider>();
        services.AddScoped<IShippingProvider, DemoShippingProvider>();
        return services;
    }
}
