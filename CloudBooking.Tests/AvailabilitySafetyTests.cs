using AngryMonkey.CloudBooking;

namespace CloudBooking.Tests;

public class AvailabilitySafetyTests
{
    [Fact]
    public async Task Daily_schedule_does_not_allow_a_late_slot_to_wrap_past_midnight()
    {
        InMemoryBookingStore store = new();
        Guid resource = Guid.NewGuid();
        await store.SaveResourceAsync(new(resource, "Room", 1, "UTC"));
        await store.SaveAvailabilityRulesAsync(resource, [new(DayOfWeek.Monday, new(9, 0), new(17, 0))]);
        AvailabilityService availability = new(store);
        DateTimeOffset start = new(2026, 9, 21, 23, 0, 0, TimeSpan.Zero);
        Assert.False(await availability.IsAvailableAsync(resource, start, start.AddHours(1), 1));
        IReadOnlyList<TimeSlot> slots = await availability.GetAvailableSlotsAsync(new(resource, new("session", "Session", TimeSpan.FromHours(1)), start.Date, start.Date.AddDays(1)));
        Assert.All(slots, slot => Assert.InRange(slot.StartsAt.UtcDateTime.Hour, 9, 16));
    }

    [Theory]
    [InlineData(0, 30)]
    [InlineData(-1, 30)]
    [InlineData(30, 0)]
    [InlineData(30, -1)]
    public async Task Invalid_duration_or_interval_is_rejected(int duration, int interval)
    {
        AvailabilityService availability = new(new InMemoryBookingStore());
        DateTimeOffset start = new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => availability.GetAvailableSlotsAsync(new(Guid.NewGuid(), new("service", "Service", TimeSpan.FromMinutes(duration)), start, start.AddHours(2), TimeSpan.FromMinutes(interval))));
    }

    [Fact]
    public async Task Offered_slots_include_buffers_when_checking_schedule_and_reservations()
    {
        InMemoryBookingStore store = new();
        Guid resource = Guid.NewGuid();
        DateTimeOffset start = new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);
        await store.SaveResourceAsync(new(resource, "Room", 1, "UTC"));
        await store.SaveAvailabilityRulesAsync(resource, [new(DayOfWeek.Monday, new(9, 0), new(12, 0))]);
        AvailabilityService availability = new(store);
        BookingService booking = new(store, availability);
        BookingServiceReference service = new("session", "Session", TimeSpan.FromHours(1), BufferAfter: TimeSpan.FromMinutes(15));
        IReadOnlyList<TimeSlot> initial = await availability.GetAvailableSlotsAsync(new(resource, service, start, start.AddHours(3), TimeSpan.FromMinutes(15)));
        Assert.Contains(initial, slot => slot.StartsAt == start);
        Assert.DoesNotContain(initial, slot => slot.StartsAt == start.AddHours(2));
        await booking.ReserveAsync(new(resource, service, start.AddHours(1)));
        IReadOnlyList<TimeSlot> slots = await availability.GetAvailableSlotsAsync(new(resource, service, start, start.AddHours(3), TimeSpan.FromMinutes(15)));
        Assert.DoesNotContain(slots, slot => slot.StartsAt == start);
        Assert.DoesNotContain(slots, slot => slot.StartsAt == start.AddHours(2));
        foreach (TimeSlot slot in slots)
            Assert.True(await availability.IsAvailableAsync(resource, slot.StartsAt, slot.EndsAt.AddMinutes(15), 1));
    }
}
