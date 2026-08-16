# CloudBooking standalone scheduling and reservations

CloudBooking supports service businesses, appointments, events, rentals, rooms, and other capacity-based reservations without requiring a catalog or product entity.

## Booking responsibilities

Availability rules are evaluated in the resource TimeZoneId when supplied, while reservations and blackouts retain absolute DateTimeOffset values.

The public contracts cover resources, application-owned service references, weekly availability, blackout periods, time slots, capacity, buffers, reservations, cancellation, confirmation, and rescheduling. `IAvailabilityService` and `IBookingService` run without persistence infrastructure through `InMemoryBookingStore`.

## Optional payment integration

CloudBooking remains functional without CloudPayments. Applications that require a deposit or authorization may implement `IBookingPaymentCoordinator`; the core stores only an opaque payment reference and never communicates with a provider directly.

## Persistence and testing

`IBookingStore` is the persistence boundary. Unit tests exercise scheduling and conflict behavior without external services. The private CDM adapter maps reservations and resources to CDM records when CDM-backed persistence is selected. No Azure Table adapter is included; storage remains an application choice.

## What CloudBooking does not own

CloudBooking does not own products, carts, subscriptions, payment providers, or application-specific service semantics.

See the [ecosystem architecture](../../docs/commerce-ecosystem/index.md), [CloudPayments](../../CloudPayments/docs/index.md), and [CloudCommerce](../../CloudCommerce/docs/index.md).
