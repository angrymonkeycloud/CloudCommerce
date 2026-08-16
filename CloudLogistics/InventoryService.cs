using System.Collections.Concurrent;
using AngryMonkey.Cloud;
using AngryMonkey.Cloud.Geography;

namespace AngryMonkey.CloudLogistics;

public sealed class InMemoryInventoryStore : IInventoryStore
{
    private readonly ConcurrentDictionary<(string Item, string? Variant, Guid Warehouse), InventoryLevel> _levels = new();
    private readonly ConcurrentDictionary<Guid, InventoryReservation> _reservations = new();
    private readonly ConcurrentQueue<StockMovement> _movements = new();

    public Task<InventoryLevel?> GetAsync(InventoryItemReference item, Guid warehouseId, CancellationToken cancellationToken = default)
    {
        _levels.TryGetValue((item.Reference, item.VariantReference, warehouseId), out InventoryLevel? level);
        return Task.FromResult(level);
    }

    public Task SaveAsync(InventoryLevel level, CancellationToken cancellationToken = default)
    {
        _levels[(level.Item.Reference, level.Item.VariantReference, level.WarehouseId)] = level;
        return Task.CompletedTask;
    }

    public Task SaveReservationAsync(InventoryReservation reservation, CancellationToken cancellationToken = default)
    {
        _reservations[reservation.Id] = reservation;
        return Task.CompletedTask;
    }

    public Task<InventoryReservation?> GetReservationAsync(Guid reservationId, CancellationToken cancellationToken = default)
    {
        _reservations.TryGetValue(reservationId, out InventoryReservation? reservation);
        return Task.FromResult(reservation);
    }

    public Task DeleteReservationAsync(Guid reservationId, CancellationToken cancellationToken = default)
    {
        _reservations.TryRemove(reservationId, out _);
        return Task.CompletedTask;
    }

    public Task SaveMovementAsync(StockMovement movement, CancellationToken cancellationToken = default)
    {
        _movements.Enqueue(movement);
        return Task.CompletedTask;
    }
}

public sealed class InventoryService(IInventoryStore store) : IInventoryService
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<InventoryLevel> GetAvailabilityAsync(InventoryItemReference item, Guid warehouseId, CancellationToken cancellationToken = default)
        => await store.GetAsync(item, warehouseId, cancellationToken) ?? new() { Item = item, WarehouseId = warehouseId };

    public async Task<InventoryLevel> SetStockAsync(InventoryItemReference item, Guid warehouseId, int onHand, CancellationToken cancellationToken = default)
    {
        if (onHand < 0)
            throw new ArgumentOutOfRangeException(nameof(onHand));

        InventoryLevel current = await GetAvailabilityAsync(item, warehouseId, cancellationToken);
        if (onHand < current.Reserved)
            throw new InvalidOperationException("On-hand stock cannot be lower than reserved stock.");

        InventoryLevel updated = Copy(current, onHand: onHand);
        await store.SaveAsync(updated, cancellationToken);
        return updated;
    }

    public async Task<InventoryReservation> ReserveAsync(InventoryItemReference item, Guid warehouseId, int quantity, TimeSpan duration, string? ownerReference = null, CancellationToken cancellationToken = default)
    {
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity));
        if (duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration));

        await _gate.WaitAsync(cancellationToken);
        try
        {
            InventoryLevel current = await GetAvailabilityAsync(item, warehouseId, cancellationToken);
            if (current.Available < quantity)
                throw new InvalidOperationException("Insufficient inventory is available for this reservation.");

            InventoryReservation reservation = new(Guid.NewGuid(), item, warehouseId, quantity, DateTimeOffset.UtcNow.Add(duration), ownerReference);
            await store.SaveAsync(Copy(current, reserved: current.Reserved + quantity), cancellationToken);
            await store.SaveReservationAsync(reservation, cancellationToken);
            await store.SaveMovementAsync(new(Guid.NewGuid(), item, warehouseId, -quantity, StockMovementTypes.Reservation, DateTimeOffset.UtcNow, reservation.Id.ToString()), cancellationToken);
            return reservation;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ReleaseAsync(Guid reservationId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            InventoryReservation? reservation = await store.GetReservationAsync(reservationId, cancellationToken);
            if (reservation is null)
                return;

            InventoryLevel current = await GetAvailabilityAsync(reservation.Item, reservation.WarehouseId, cancellationToken);
            await store.SaveAsync(Copy(current, reserved: Math.Max(0, current.Reserved - reservation.Quantity)), cancellationToken);
            await store.DeleteReservationAsync(reservationId, cancellationToken);
            await store.SaveMovementAsync(new(Guid.NewGuid(), reservation.Item, reservation.WarehouseId, reservation.Quantity, StockMovementTypes.ReservationRelease, DateTimeOffset.UtcNow, reservationId.ToString()), cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<InventoryLevel> AdjustAsync(InventoryItemReference item, Guid warehouseId, int quantity, string? reference = null, CancellationToken cancellationToken = default)
    {
        InventoryLevel current = await GetAvailabilityAsync(item, warehouseId, cancellationToken);
        int onHand = current.OnHand + quantity;
        if (onHand < current.Reserved)
            throw new InvalidOperationException("The adjustment would reduce available stock below zero.");

        InventoryLevel updated = Copy(current, onHand: onHand);
        await store.SaveAsync(updated, cancellationToken);
        await store.SaveMovementAsync(new(Guid.NewGuid(), item, warehouseId, quantity, StockMovementTypes.Adjustment, DateTimeOffset.UtcNow, reference), cancellationToken);
        return updated;
    }

    public async Task TransferAsync(StockTransfer transfer, CancellationToken cancellationToken = default)
    {
        if (transfer.Quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(transfer));

        await AdjustAsync(transfer.Item, transfer.FromWarehouseId, -transfer.Quantity, transfer.Id.ToString(), cancellationToken);
        await AdjustAsync(transfer.Item, transfer.ToWarehouseId, transfer.Quantity, transfer.Id.ToString(), cancellationToken);
    }

    private static InventoryLevel Copy(InventoryLevel level, int? onHand = null, int? reserved = null) => new()
    {
        Item = level.Item,
        WarehouseId = level.WarehouseId,
        OnHand = onHand ?? level.OnHand,
        Reserved = reserved ?? level.Reserved,
        Version = level.Version + 1
    };
}

public sealed class ShippingProviderRegistry(IEnumerable<IShippingProvider> providers) : IShippingProviderRegistry
{
    private readonly IReadOnlyDictionary<string, IShippingProvider> _providers = providers.ToDictionary(provider => provider.Name, StringComparer.OrdinalIgnoreCase);
    public IReadOnlyCollection<IShippingProvider> Providers => [.. _providers.Values];
    public IShippingProvider Get(string name) => _providers.TryGetValue(name, out IShippingProvider? provider) ? provider : throw new InvalidOperationException($"Shipping provider '{name}' is not registered.");
}

public sealed class CloudGeographyAddressValidator(CloudGeographyClient geography) : IGeographicAddressValidator
{
    public GeographyValidationResult Validate(LogisticsAddress address)
    {
        Country? country = geography.Countries.Get(address.CountryCode);
        if (country is null)
            return new(false, $"Country code '{address.CountryCode}' is not recognized by CloudGeography.");

        if (string.IsNullOrWhiteSpace(address.SubdivisionCode))
            return new(true);

        Subdivision? subdivision = geography.Subdivisions.Get(country.Code, address.SubdivisionCode);
        if (subdivision is null)
            return new(false, $"Subdivision code '{address.SubdivisionCode}' is not valid for '{country.Code}'.");

        if (!string.IsNullOrWhiteSpace(address.SubdivisionChildCode) && geography.Subdivisions.GetChild(country.Code, subdivision.Code, address.SubdivisionChildCode) is null)
            return new(false, $"Subdivision child code '{address.SubdivisionChildCode}' is not valid below '{subdivision.Code}'.");

        return new(true);
    }
}
