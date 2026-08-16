using AngryMonkey.CloudBooking;

namespace CloudBooking.Tests;

public class AvailabilityAndLifecycleTests
{
    [Fact]
    public async Task IsAvailableAsync_without_rules_is_available()
    {
        InMemoryBookingStore store = new();
        Guid resource = Guid.NewGuid();
        await store.SaveResourceAsync(new(resource, "Room"));

        bool available = await new AvailabilityService(store).IsAvailableAsync(resource, DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(1).AddHours(1), 1);

        Assert.True(available);
    }

    [Fact]
    public async Task IsAvailableAsync_overlapping_blackout_is_unavailable()
    {
        InMemoryBookingStore store = new();
        Guid resource = Guid.NewGuid();
        DateTimeOffset start = DateTimeOffset.UtcNow.AddDays(1);
        await store.SaveResourceAsync(new(resource, "Room"));
        await store.SaveBlackoutAsync(resource, new(start, start.AddHours(2), "Maintenance"));

        Assert.False(await new AvailabilityService(store).IsAvailableAsync(resource, start.AddMinutes(30), start.AddHours(1), 1));
    }

    [Fact]
    public async Task GetAvailableSlotsAsync_uses_requested_interval()
    {
        InMemoryBookingStore store = new();
        Guid resource = Guid.NewGuid();
        DateTimeOffset start = new(2026, 8, 17, 9, 0, 0, TimeSpan.Zero);
        await store.SaveResourceAsync(new(resource, "Room"));

        IReadOnlyList<TimeSlot> slots = await new AvailabilityService(store).GetAvailableSlotsAsync(new(resource, new("service", "Service", TimeSpan.FromMinutes(30)), start, start.AddHours(1), TimeSpan.FromMinutes(30)));

        Assert.Equal(2, slots.Count);
    }

    [Fact]
    public async Task ReserveAsync_buffer_blocks_adjacent_booking()
    {
        InMemoryBookingStore store = new();
        Guid resource = Guid.NewGuid();
        DateTimeOffset start = DateTimeOffset.UtcNow.AddDays(1);
        await store.SaveResourceAsync(new(resource, "Chair"));
        BookingService booking = new(store, new AvailabilityService(store));
        BookingServiceReference service = new("cut", "Haircut", TimeSpan.FromMinutes(30), BufferAfter: TimeSpan.FromMinutes(15));
        await booking.ReserveAsync(new(resource, service, start));

        await Assert.ThrowsAsync<InvalidOperationException>(() => booking.ReserveAsync(new(resource, service, start.AddMinutes(35))));
    }

    [Fact]
    public async Task CancelAsync_cancels_authorized_payment()
    {
        InMemoryBookingStore store = new();
        Guid resource = Guid.NewGuid();
        await store.SaveResourceAsync(new(resource, "Chair"));
        RecordingPayment payment = new();
        BookingService booking = new(store, new AvailabilityService(store), payment);
        Reservation reservation = await booking.ReserveAsync(new(resource, new("cut", "Cut", TimeSpan.FromMinutes(30)), DateTimeOffset.UtcNow.AddDays(1)));

        Reservation cancelled = await booking.CancelAsync(reservation.Id);

        Assert.Equal(ReservationStatuses.Cancelled, cancelled.Status);
        Assert.Equal("payment-1", payment.CancelledReference);
    }

    [Fact]
    public async Task ConfirmAsync_missing_reservation_throws()
    {
        BookingService booking = new(new InMemoryBookingStore(), new AvailabilityService(new InMemoryBookingStore()));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => booking.ConfirmAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task ReserveAsync_non_positive_capacity_throws()
    {
        InMemoryBookingStore store = new();
        BookingService booking = new(store, new AvailabilityService(store));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => booking.ReserveAsync(new(Guid.NewGuid(), new("service", "Service", TimeSpan.FromMinutes(30)), DateTimeOffset.UtcNow, 0)));
    }

    private sealed class RecordingPayment : IBookingPaymentCoordinator
    {
        public string? CancelledReference { get; private set; }
        public Task<string?> AuthorizeAsync(Reservation reservation, CancellationToken cancellationToken = default) => Task.FromResult<string?>("payment-1");
        public Task CancelAsync(string paymentReference, CancellationToken cancellationToken = default) { CancelledReference = paymentReference; return Task.CompletedTask; }
    }
}
