using AngryMonkey.CloudBooking;

namespace AngryMonkey.CloudCommerce;

public sealed class CloudBookingCommerceGateway(IBookingService bookings) : ICommerceBookingGateway
{
    public async Task ConfirmAsync(IReadOnlyList<Guid> reservationIds, string? paymentReference, CancellationToken cancellationToken = default)
    {
        foreach (Guid reservationId in reservationIds)
            await bookings.ConfirmAsync(reservationId, paymentReference, cancellationToken);
    }

    public async Task CancelAsync(IReadOnlyList<Guid> reservationIds, CancellationToken cancellationToken = default)
    {
        foreach (Guid reservationId in reservationIds)
            await bookings.CancelAsync(reservationId, cancellationToken);
    }
}
