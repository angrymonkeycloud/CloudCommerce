using AngryMonkey.CloudBooking;

namespace CloudBooking.Tests;

public class ReschedulingTests
{
    [Fact]
    public async Task RescheduleAsync_same_slot_excludes_current_reservation_from_capacity()
    {
        InMemoryBookingStore store = new();
        Guid resourceId = Guid.NewGuid();
        await store.SaveResourceAsync(new(resourceId, "Chair", 1));
        AvailabilityService availability = new(store);
        BookingService service = new(store, availability);
        DateTimeOffset startsAt = DateTimeOffset.UtcNow.AddDays(1);
        Reservation reservation = await service.ReserveAsync(new(resourceId, new("cut", "Haircut", TimeSpan.FromMinutes(30)), startsAt));

        Reservation rescheduled = await service.RescheduleAsync(new(reservation.Id, startsAt));

        Assert.Equal(startsAt, rescheduled.StartsAt);
    }
}
