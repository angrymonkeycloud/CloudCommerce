namespace AngryMonkey.CloudBooking;

public interface IBookingStore
{
    Task<BookingResource?> GetResourceAsync(Guid resourceId, CancellationToken cancellationToken = default);
    Task SaveResourceAsync(BookingResource resource, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AvailabilityRule>> GetAvailabilityRulesAsync(Guid resourceId, CancellationToken cancellationToken = default);
    Task SaveAvailabilityRulesAsync(Guid resourceId, IReadOnlyList<AvailabilityRule> rules, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BlackoutPeriod>> GetBlackoutsAsync(Guid resourceId, DateTimeOffset rangeStart, DateTimeOffset rangeEnd, CancellationToken cancellationToken = default);
    Task SaveBlackoutAsync(Guid resourceId, BlackoutPeriod blackout, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Reservation>> GetReservationsAsync(Guid resourceId, DateTimeOffset rangeStart, DateTimeOffset rangeEnd, CancellationToken cancellationToken = default);
    Task<Reservation?> GetReservationAsync(Guid reservationId, CancellationToken cancellationToken = default);
    Task SaveReservationAsync(Reservation reservation, CancellationToken cancellationToken = default);
}

public interface IAvailabilityService
{
    Task<IReadOnlyList<TimeSlot>> GetAvailableSlotsAsync(AvailabilityRequest request, CancellationToken cancellationToken = default);
    Task<bool> IsAvailableAsync(Guid resourceId, DateTimeOffset startsAt, DateTimeOffset endsAt, int capacity, CancellationToken cancellationToken = default, Guid? excludedReservationId = null);
}

public interface IBookingService
{
    Task<Reservation> ReserveAsync(ReservationRequest request, CancellationToken cancellationToken = default);
    Task<Reservation> ConfirmAsync(Guid reservationId, string? paymentReference = null, CancellationToken cancellationToken = default);
    Task<Reservation> CancelAsync(Guid reservationId, CancellationToken cancellationToken = default);
    Task<Reservation> RescheduleAsync(RescheduleRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Optional application hook. CloudBooking remains fully functional when no implementation is registered.</summary>
public interface IBookingPaymentCoordinator
{
    Task<string?> AuthorizeAsync(Reservation reservation, CancellationToken cancellationToken = default);
    Task CancelAsync(string paymentReference, CancellationToken cancellationToken = default);
}
