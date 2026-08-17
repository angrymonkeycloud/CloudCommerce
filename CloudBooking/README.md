# CloudBooking

CloudBooking is a standalone scheduling and reservation foundation for appointments, hospitality, rentals, events, and other capacity-based workflows. It does not require a product, catalog, payment provider, CloudCommerce, or database package.

## Register and use

```csharp
builder.Services.AddCloudBooking();

IReadOnlyList<TimeSlot> slots = await availability.GetAvailableSlotsAsync(
    new AvailabilityRequest(resourceId, service, rangeStart, rangeEnd));

Reservation reservation = await booking.ReserveAsync(
    new ReservationRequest(resourceId, service, selectedSlot.StartsAt));
```

## Responsibilities

CloudBooking owns resources, service references, schedules, recurring availability, time slots, capacity, blackout periods, buffers, reservations, confirmation, cancellation, rescheduling, and time-zone-aware calculations. `IBookingPaymentCoordinator` is optional; booking remains fully functional without payments.

## Persistence boundary

`IBookingStore` is the public persistence seam. The default registration is suitable for local and unit-test workflows. A production application can implement the store with its database of choice without changing the booking domain or forcing a storage dependency on every consumer. Azure Table Storage is intentionally not bundled into CloudBooking.

## Testing

Availability and reservation tests run entirely in memory and cover collisions, capacity release, blackout periods, buffers, recurring schedules, cancellation, and rescheduling. See the [interactive booking demo](../CloudCommerce.Demo/README.md), the [booking reference](docs/index.md), and optional [CloudCommerce orchestration](../CloudCommerce/README.md).

CloudBooking is part of [Angry Monkey Cloud](https://angrymonkeycloud.com). Development follows the shared [AI instructions](https://github.com/angrymonkeycloud/CloudDocs/blob/main/docs/ai/instructions.md).