using AngryMonkey.Cloud.Geography;
using AngryMonkey.CloudBooking;
using AngryMonkey.CloudCommerce;

namespace CloudCommerce.Tests;

public class BookingCompositionTests
{
    [Fact]
    public async Task CheckoutAsync_booking_gateway_confirms_reservation()
    {
        RecordingBookingService bookingService = new();
        CommerceService service = new(new InMemoryCartStore(), new InMemoryOrderStore(), [], bookings: new CloudBookingCommerceGateway(bookingService));
        Cart cart = await service.CreateCartAsync("USD");
        await service.AddItemAsync(cart.Id, new("service", "Consultation", new Money("USD", 30m)));
        Guid reservationId = Guid.NewGuid();

        Order order = await service.CheckoutAsync(new() { CartId = cart.Id, IdempotencyKey = "booking-1", BookingReservationIds = [reservationId] });

        Assert.Contains(reservationId, order.BookingReservationIds);
        Assert.Equal(reservationId, bookingService.ConfirmedReservationId);
    }

    private sealed class RecordingBookingService : IBookingService
    {
        public Guid? ConfirmedReservationId { get; private set; }
        public Task<Reservation> ReserveAsync(ReservationRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Reservation> ConfirmAsync(Guid reservationId, string? paymentReference = null, CancellationToken cancellationToken = default) { ConfirmedReservationId = reservationId; return Task.FromResult(Reservation(reservationId, ReservationStatuses.Confirmed)); }
        public Task<Reservation> CancelAsync(Guid reservationId, CancellationToken cancellationToken = default) => Task.FromResult(Reservation(reservationId, ReservationStatuses.Cancelled));
        public Task<Reservation> RescheduleAsync(RescheduleRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        private static Reservation Reservation(Guid id, ReservationStatuses status) => new() { Id = id, ResourceId = Guid.NewGuid(), Service = new("service", "Service", TimeSpan.FromMinutes(30)), StartsAt = DateTimeOffset.UtcNow, EndsAt = DateTimeOffset.UtcNow.AddMinutes(30), Status = status };
    }
}
