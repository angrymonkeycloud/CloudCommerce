using System.Collections.Concurrent;

namespace AngryMonkey.CloudBooking;

public sealed class InMemoryBookingStore : IBookingStore
{
    private readonly ConcurrentDictionary<Guid, BookingResource> _resources = new();
    private readonly ConcurrentDictionary<Guid, IReadOnlyList<AvailabilityRule>> _rules = new();
    private readonly ConcurrentDictionary<Guid, ConcurrentBag<BlackoutPeriod>> _blackouts = new();
    private readonly ConcurrentDictionary<Guid, Reservation> _reservations = new();

    public Task<BookingResource?> GetResourceAsync(Guid resourceId, CancellationToken cancellationToken = default)
    {
        _resources.TryGetValue(resourceId, out BookingResource? resource);
        return Task.FromResult(resource);
    }

    public Task SaveResourceAsync(BookingResource resource, CancellationToken cancellationToken = default)
    {
        _resources[resource.Id] = resource;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AvailabilityRule>> GetAvailabilityRulesAsync(Guid resourceId, CancellationToken cancellationToken = default)
        => Task.FromResult(_rules.TryGetValue(resourceId, out IReadOnlyList<AvailabilityRule>? rules) ? rules : (IReadOnlyList<AvailabilityRule>)[]);

    public Task SaveAvailabilityRulesAsync(Guid resourceId, IReadOnlyList<AvailabilityRule> rules, CancellationToken cancellationToken = default)
    {
        _rules[resourceId] = rules;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<BlackoutPeriod>> GetBlackoutsAsync(Guid resourceId, DateTimeOffset rangeStart, DateTimeOffset rangeEnd, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<BlackoutPeriod> result = _blackouts.TryGetValue(resourceId, out ConcurrentBag<BlackoutPeriod>? periods)
            ? [.. periods.Where(period => period.StartsAt < rangeEnd && period.EndsAt > rangeStart)]
            : [];
        return Task.FromResult(result);
    }

    public Task SaveBlackoutAsync(Guid resourceId, BlackoutPeriod blackout, CancellationToken cancellationToken = default)
    {
        _blackouts.GetOrAdd(resourceId, _ => []).Add(blackout);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Reservation>> GetReservationsAsync(Guid resourceId, DateTimeOffset rangeStart, DateTimeOffset rangeEnd, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Reservation> result = [.. _reservations.Values.Where(reservation => reservation.ResourceId == resourceId && reservation.Status is not ReservationStatuses.Cancelled && (reservation.OccupancyStartsAt ?? reservation.StartsAt) < rangeEnd && (reservation.OccupancyEndsAt ?? reservation.EndsAt) > rangeStart)];
        return Task.FromResult(result);
    }

    public Task<Reservation?> GetReservationAsync(Guid reservationId, CancellationToken cancellationToken = default)
    {
        _reservations.TryGetValue(reservationId, out Reservation? reservation);
        return Task.FromResult(reservation);
    }

    public Task SaveReservationAsync(Reservation reservation, CancellationToken cancellationToken = default)
    {
        _reservations[reservation.Id] = reservation;
        return Task.CompletedTask;
    }
}

public sealed class AvailabilityService(IBookingStore store) : IAvailabilityService
{
    public async Task<IReadOnlyList<TimeSlot>> GetAvailableSlotsAsync(AvailabilityRequest request, CancellationToken cancellationToken = default)
    {
        if (request.RangeEnd <= request.RangeStart)
            throw new ArgumentException("The availability range end must be after its start.", nameof(request));

        TimeSpan interval = request.SlotInterval ?? TimeSpan.FromMinutes(15);

        if (interval <= TimeSpan.Zero || request.Service.Duration <= TimeSpan.Zero || request.Service.CapacityRequired <= 0 || request.Service.BufferBefore < TimeSpan.Zero || request.Service.BufferAfter < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(request), "Duration, interval and capacity must be positive; buffers cannot be negative.");

        List<TimeSlot> slots = [];
        DateTimeOffset candidate = request.RangeStart;

        while (candidate + request.Service.Duration <= request.RangeEnd)
        {
            DateTimeOffset endsAt = candidate + request.Service.Duration;
            cancellationToken.ThrowIfCancellationRequested();
            DateTimeOffset occupancyStart = candidate - (request.Service.BufferBefore ?? TimeSpan.Zero);
            DateTimeOffset occupancyEnd = endsAt + (request.Service.BufferAfter ?? TimeSpan.Zero);
            int remainingCapacity = await GetRemainingCapacityAsync(request.ResourceId, occupancyStart, occupancyEnd, cancellationToken);

            if (remainingCapacity >= request.Service.CapacityRequired && await IsInsideScheduleAsync(request.ResourceId, occupancyStart, occupancyEnd, cancellationToken))
                slots.Add(new(request.ResourceId, candidate, endsAt, remainingCapacity));

            candidate += interval;
        }

        return slots;
    }

    public async Task<bool> IsAvailableAsync(Guid resourceId, DateTimeOffset startsAt, DateTimeOffset endsAt, int capacity, CancellationToken cancellationToken = default, Guid? excludedReservationId = null)
        => capacity > 0 && await IsInsideScheduleAsync(resourceId, startsAt, endsAt, cancellationToken) && await GetRemainingCapacityAsync(resourceId, startsAt, endsAt, cancellationToken, excludedReservationId) >= capacity;

    private async Task<int> GetRemainingCapacityAsync(Guid resourceId, DateTimeOffset startsAt, DateTimeOffset endsAt, CancellationToken cancellationToken, Guid? excludedReservationId = null)
    {
        BookingResource resource = await store.GetResourceAsync(resourceId, cancellationToken) ?? throw new InvalidOperationException($"Booking resource '{resourceId}' does not exist.");
        IReadOnlyList<Reservation> reservations = await store.GetReservationsAsync(resourceId, startsAt, endsAt, cancellationToken);
        return resource.Capacity - reservations.Where(reservation => reservation.Id != excludedReservationId).Sum(reservation => reservation.Capacity);
    }

    private async Task<bool> IsInsideScheduleAsync(Guid resourceId, DateTimeOffset startsAt, DateTimeOffset endsAt, CancellationToken cancellationToken)
    {
        IReadOnlyList<BlackoutPeriod> blackouts = await store.GetBlackoutsAsync(resourceId, startsAt, endsAt, cancellationToken);
        if (blackouts.Count > 0)
            return false;

        IReadOnlyList<AvailabilityRule> rules = await store.GetAvailabilityRulesAsync(resourceId, cancellationToken);
        if (rules.Count == 0)
            return true;

        BookingResource resource = await store.GetResourceAsync(resourceId, cancellationToken) ?? throw new InvalidOperationException($"Booking resource '{resourceId}' does not exist.");
        TimeZoneInfo? timeZone = string.IsNullOrWhiteSpace(resource.TimeZoneId) ? null : TimeZoneInfo.FindSystemTimeZoneById(resource.TimeZoneId);
        DateTimeOffset localStart = timeZone is null ? startsAt : TimeZoneInfo.ConvertTime(startsAt, timeZone);
        DateTimeOffset localEnd = timeZone is null ? endsAt : TimeZoneInfo.ConvertTime(endsAt, timeZone);

        if (localStart.Date != localEnd.Date)
            return false;

        DateOnly date = DateOnly.FromDateTime(localStart.Date);
        TimeOnly start = TimeOnly.FromDateTime(localStart.DateTime);
        TimeOnly end = TimeOnly.FromDateTime(localEnd.DateTime);

        return rules.Any(rule => rule.Type == AvailabilityRuleTypes.Available && rule.Day == localStart.DayOfWeek && (rule.EffectiveFrom is null || date >= rule.EffectiveFrom) && (rule.EffectiveTo is null || date <= rule.EffectiveTo) && start >= rule.StartsAt && end <= rule.EndsAt)
            && !rules.Any(rule => rule.Type == AvailabilityRuleTypes.Unavailable && rule.Day == localStart.DayOfWeek && start < rule.EndsAt && end > rule.StartsAt);
    }
}

public sealed class BookingService(IBookingStore store, IAvailabilityService availability, IBookingPaymentCoordinator? payments = null) : IBookingService
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<Reservation> ReserveAsync(ReservationRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Capacity <= 0 || request.Service.Duration <= TimeSpan.Zero || request.Service.BufferBefore < TimeSpan.Zero || request.Service.BufferAfter < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(request));

        DateTimeOffset startsAt = request.StartsAt - (request.Service.BufferBefore ?? TimeSpan.Zero);
        DateTimeOffset endsAt = request.StartsAt + request.Service.Duration + (request.Service.BufferAfter ?? TimeSpan.Zero);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!await availability.IsAvailableAsync(request.ResourceId, startsAt, endsAt, request.Capacity, cancellationToken))
                throw new InvalidOperationException("The requested booking time is not available.");

            Reservation reservation = new()
            {
                ResourceId = request.ResourceId,
                Service = request.Service,
                StartsAt = request.StartsAt,
                EndsAt = request.StartsAt + request.Service.Duration,
                OccupancyStartsAt = startsAt,
                OccupancyEndsAt = endsAt,
                Capacity = request.Capacity,
                CustomerReference = request.CustomerReference,
                Metadata = request.Metadata ?? []
            };

            string? paymentReference = payments is null ? null : await payments.AuthorizeAsync(reservation, cancellationToken);
            reservation = Copy(reservation, paymentReference: paymentReference);
            await store.SaveReservationAsync(reservation, cancellationToken);
            return reservation;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Reservation> ConfirmAsync(Guid reservationId, string? paymentReference = null, CancellationToken cancellationToken = default)
    {
        Reservation current = await GetRequiredAsync(reservationId, cancellationToken);
        Reservation updated = Copy(current, status: ReservationStatuses.Confirmed, paymentReference: paymentReference ?? current.PaymentReference);
        await store.SaveReservationAsync(updated, cancellationToken);
        return updated;
    }

    public async Task<Reservation> CancelAsync(Guid reservationId, CancellationToken cancellationToken = default)
    {
        Reservation current = await GetRequiredAsync(reservationId, cancellationToken);
        if (payments is not null && !string.IsNullOrWhiteSpace(current.PaymentReference))
            await payments.CancelAsync(current.PaymentReference, cancellationToken);

        Reservation updated = Copy(current, status: ReservationStatuses.Cancelled);
        await store.SaveReservationAsync(updated, cancellationToken);
        return updated;
    }

    public async Task<Reservation> RescheduleAsync(RescheduleRequest request, CancellationToken cancellationToken = default)
    {
        Reservation current = await GetRequiredAsync(request.ReservationId, cancellationToken);
        DateTimeOffset occupancyStartsAt = request.StartsAt - (current.Service.BufferBefore ?? TimeSpan.Zero);
        DateTimeOffset occupancyEndsAt = request.StartsAt + current.Service.Duration + (current.Service.BufferAfter ?? TimeSpan.Zero);
        if (!await availability.IsAvailableAsync(current.ResourceId, occupancyStartsAt, occupancyEndsAt, current.Capacity, cancellationToken, current.Id))
            throw new InvalidOperationException("The requested booking time is not available.");

        Reservation updated = Copy(current, startsAt: request.StartsAt, endsAt: request.StartsAt + current.Service.Duration, occupancyStartsAt: occupancyStartsAt, occupancyEndsAt: occupancyEndsAt);
        await store.SaveReservationAsync(updated, cancellationToken);
        return updated;
    }

    private async Task<Reservation> GetRequiredAsync(Guid reservationId, CancellationToken cancellationToken)
        => await store.GetReservationAsync(reservationId, cancellationToken) ?? throw new KeyNotFoundException($"Reservation '{reservationId}' was not found.");

    private static Reservation Copy(Reservation reservation, ReservationStatuses? status = null, DateTimeOffset? startsAt = null, DateTimeOffset? endsAt = null, DateTimeOffset? occupancyStartsAt = null, DateTimeOffset? occupancyEndsAt = null, string? paymentReference = null) => new()
    {
        Id = reservation.Id,
        ResourceId = reservation.ResourceId,
        Service = reservation.Service,
        StartsAt = startsAt ?? reservation.StartsAt,
        EndsAt = endsAt ?? reservation.EndsAt,
        OccupancyStartsAt = occupancyStartsAt ?? reservation.OccupancyStartsAt,
        OccupancyEndsAt = occupancyEndsAt ?? reservation.OccupancyEndsAt,
        Capacity = reservation.Capacity,
        Status = status ?? reservation.Status,
        CustomerReference = reservation.CustomerReference,
        PaymentReference = paymentReference ?? reservation.PaymentReference,
        CreatedAt = reservation.CreatedAt,
        Metadata = reservation.Metadata
    };
}
