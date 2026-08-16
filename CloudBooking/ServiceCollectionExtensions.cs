using AngryMonkey.CloudBooking;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

public static class CloudBookingServiceCollectionExtensions
{
    public static IServiceCollection AddCloudBooking(this IServiceCollection services)
    {
        services.TryAddSingleton<IBookingStore, InMemoryBookingStore>();
        services.TryAddScoped<IAvailabilityService, AvailabilityService>();
        services.TryAddScoped<IBookingService, BookingService>();
        return services;
    }
}
