using AngryMonkey.CloudBooking;

namespace CloudBooking.Tests;

public class AdditionalCoverageTests
{
    [Fact]
    public async Task CancelAsync_releases_capacity_for_same_slot()
    {
        InMemoryBookingStore store = new();
        Guid resource = Guid.NewGuid();
        DateTimeOffset start = DateTimeOffset.UtcNow.AddDays(1);
        await store.SaveResourceAsync(new(resource, "Room", 1));
        BookingService booking = new(store, new AvailabilityService(store));
        BookingServiceReference service = new("meeting", "Meeting", TimeSpan.FromHours(1));
        Reservation reservation = await booking.ReserveAsync(new(resource, service, start));

        await booking.CancelAsync(reservation.Id);
        Reservation replacement = await booking.ReserveAsync(new(resource, service, start));

        Assert.Equal(ReservationStatuses.Pending, replacement.Status);
    }

    [Fact]
    public async Task Unavailable_rule_overrides_matching_available_rule()
    {
        InMemoryBookingStore store = new();
        Guid resource = Guid.NewGuid();
        DateTimeOffset start = Next(DayOfWeek.Monday, 10);
        await store.SaveResourceAsync(new(resource, "Studio"));
        await store.SaveAvailabilityRulesAsync(resource, [new(DayOfWeek.Monday, new(9, 0), new(17, 0)), new(DayOfWeek.Monday, new(10, 0), new(11, 0), AvailabilityRuleTypes.Unavailable)]);

        bool available = await new AvailabilityService(store).IsAvailableAsync(resource, start, start.AddMinutes(30), 1);

        Assert.False(available);
    }

    [Fact]
    public async Task GetAvailableSlotsAsync_invalid_range_throws()
    {
        InMemoryBookingStore store = new();
        Guid resource = Guid.NewGuid();
        DateTimeOffset start = DateTimeOffset.UtcNow.AddDays(1);
        await store.SaveResourceAsync(new(resource, "Room"));

        await Assert.ThrowsAsync<ArgumentException>(() => new AvailabilityService(store).GetAvailableSlotsAsync(new(resource, new("service", "Service", TimeSpan.FromMinutes(30)), start, start)));
    }

    [Fact]
    public async Task Concurrent_reservations_respect_resource_capacity()
    {
        InMemoryBookingStore store = new();
        Guid resource = Guid.NewGuid();
        DateTimeOffset start = DateTimeOffset.UtcNow.AddDays(1);
        await store.SaveResourceAsync(new(resource, "Chair", 1));
        BookingService booking = new(store, new AvailabilityService(store));
        BookingServiceReference service = new("cut", "Cut", TimeSpan.FromMinutes(30));

        Task<Reservation>[] attempts = [booking.ReserveAsync(new(resource, service, start)), booking.ReserveAsync(new(resource, service, start))];
        Exception? failure = await Record.ExceptionAsync(() => Task.WhenAll(attempts));
        IReadOnlyList<Reservation> reservations = await store.GetReservationsAsync(resource, start, start.AddMinutes(30));

        Assert.IsType<InvalidOperationException>(failure);
        Assert.Single(reservations);
    }

    private static DateTimeOffset Next(DayOfWeek day, int hour)
    {
        DateTimeOffset candidate = DateTimeOffset.UtcNow.Date.AddDays(1).AddHours(hour);
        while (candidate.DayOfWeek != day)
            candidate = candidate.AddDays(1);
        return candidate;
    }
}