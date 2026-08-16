namespace AngryMonkey.CloudBooking;

public enum ReservationStatuses
{
    Pending,
    Confirmed,
    Cancelled,
    Completed,
    NoShow
}

public enum AvailabilityRuleTypes
{
    Available,
    Unavailable
}

public sealed record BookingResource(Guid Id, string Name, int Capacity = 1, string? TimeZoneId = null, Dictionary<string, string>? Metadata = null);
public sealed record BookingServiceReference(string Reference, string Name, TimeSpan Duration, int CapacityRequired = 1, TimeSpan? BufferBefore = null, TimeSpan? BufferAfter = null, Dictionary<string, string>? Metadata = null);
public sealed record AvailabilityRule(DayOfWeek Day, TimeOnly StartsAt, TimeOnly EndsAt, AvailabilityRuleTypes Type = AvailabilityRuleTypes.Available, DateOnly? EffectiveFrom = null, DateOnly? EffectiveTo = null);
public sealed record BlackoutPeriod(DateTimeOffset StartsAt, DateTimeOffset EndsAt, string? Reason = null);
public sealed record TimeSlot(Guid ResourceId, DateTimeOffset StartsAt, DateTimeOffset EndsAt, int RemainingCapacity);

public sealed class Reservation
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid ResourceId { get; init; }
    public required BookingServiceReference Service { get; init; }
    public required DateTimeOffset StartsAt { get; init; }
    public required DateTimeOffset EndsAt { get; init; }
    public DateTimeOffset? OccupancyStartsAt { get; init; }
    public DateTimeOffset? OccupancyEndsAt { get; init; }
    public int Capacity { get; init; } = 1;
    public ReservationStatuses Status { get; init; } = ReservationStatuses.Pending;
    public string? CustomerReference { get; init; }
    public string? PaymentReference { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public Dictionary<string, string> Metadata { get; init; } = [];
}

public sealed record AvailabilityRequest(Guid ResourceId, BookingServiceReference Service, DateTimeOffset RangeStart, DateTimeOffset RangeEnd, TimeSpan? SlotInterval = null);
public sealed record ReservationRequest(Guid ResourceId, BookingServiceReference Service, DateTimeOffset StartsAt, int Capacity = 1, string? CustomerReference = null, Dictionary<string, string>? Metadata = null);
public sealed record RescheduleRequest(Guid ReservationId, DateTimeOffset StartsAt);
