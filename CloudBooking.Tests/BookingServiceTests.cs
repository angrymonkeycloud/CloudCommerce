using AngryMonkey.CloudBooking;

namespace CloudBooking.Tests;

public class BookingServiceTests
{
    [Fact]
    public async Task ReserveAsync_capacity_is_exhausted_rejects_overlapping_reservation()
    {
        InMemoryBookingStore store = new();
        Guid resourceId = Guid.NewGuid();
        await store.SaveResourceAsync(new(resourceId, "Room", 1));
        AvailabilityService availability = new(store);
        BookingService service = new(store, availability);
        BookingServiceReference bookingService = new("consultation", "Consultation", TimeSpan.FromHours(1));
        DateTimeOffset startsAt = DateTimeOffset.UtcNow.AddDays(1);

        await service.ReserveAsync(new(resourceId, bookingService, startsAt));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReserveAsync(new(resourceId, bookingService, startsAt)));
    }
}
