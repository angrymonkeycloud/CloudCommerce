using System.Collections.Concurrent;
using AngryMonkey.Cloud.Geography;
using AngryMonkey.CloudLogistics;
using AngryMonkey.CloudPayments;

namespace AngryMonkey.CloudCommerce.Demo;

public sealed class DemoPaymentProvider : IPaymentProvider
{
    private readonly ConcurrentDictionary<string, Payment> _payments = new();
    public string Name => "Demo";
    public PaymentProviderCapabilities Capabilities => PaymentProviderCapabilities.Create | PaymentProviderCapabilities.Authorize | PaymentProviderCapabilities.Capture | PaymentProviderCapabilities.Void | PaymentProviderCapabilities.Refund | PaymentProviderCapabilities.PartialRefund | PaymentProviderCapabilities.RecurringPayments | PaymentProviderCapabilities.Webhooks;

    public Task<PaymentResult> CreateAsync(PaymentRequest request, CancellationToken cancellationToken = default) => SaveAsync(request.Amount, PaymentStatuses.Captured);
    public Task<PaymentResult> AuthorizeAsync(PaymentRequest request, CancellationToken cancellationToken = default) => SaveAsync(request.Amount, PaymentStatuses.Authorized);
    public Task<PaymentResult> CaptureAsync(CaptureRequest request, CancellationToken cancellationToken = default) => ChangeAsync(request.PaymentId, PaymentStatuses.Captured, request.Amount);
    public Task<PaymentResult> VoidAsync(VoidPaymentRequest request, CancellationToken cancellationToken = default) => ChangeAsync(request.PaymentId, PaymentStatuses.Voided);
    public Task<PaymentResult> RefundAsync(RefundRequest request, CancellationToken cancellationToken = default) => ChangeAsync(request.PaymentId, request.IsPartial ? PaymentStatuses.PartiallyRefunded : PaymentStatuses.Refunded, request.Amount);
    public Task<PaymentResult> ChargeRecurringAsync(RecurringPaymentRequest request, CancellationToken cancellationToken = default) => SaveAsync(request.Amount, PaymentStatuses.Captured);
    public Task<Payment?> GetAsync(string paymentId, CancellationToken cancellationToken = default) => Task.FromResult(_payments.GetValueOrDefault(paymentId));
    public async Task<PaymentStatuses?> GetStatusAsync(string paymentId, CancellationToken cancellationToken = default) => (await GetAsync(paymentId, cancellationToken))?.Status;
    public Task<bool> ValidateWebhookAsync(PaymentWebhookRequest request, CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task<PaymentProviderEvent> ParseWebhookAsync(PaymentWebhookRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new PaymentProviderEvent(Name, Guid.NewGuid().ToString("N"), "demo.payment", null, DateTimeOffset.UtcNow, new Dictionary<string, string>()));

    private Task<PaymentResult> SaveAsync(Money amount, PaymentStatuses status)
    {
        string id = Guid.NewGuid().ToString("N");
        Payment payment = new() { Id = id, Provider = Name, ProviderReference = id, Amount = amount, Status = status };
        _payments[id] = payment;
        return Task.FromResult(new PaymentResult(payment));
    }

    private Task<PaymentResult> ChangeAsync(string id, PaymentStatuses status, Money? amount = null)
    {
        if (!_payments.TryGetValue(id, out Payment? current))
            return Task.FromResult(PaymentResult.Failed(new(PaymentErrorTypes.InvalidRequest, "not_found", "The demo payment was not found.")));

        Payment updated = new() { Id = current.Id, Provider = Name, ProviderReference = current.ProviderReference, Amount = amount ?? current.Amount, Status = status, CreatedAt = current.CreatedAt };
        _payments[id] = updated;
        return Task.FromResult(new PaymentResult(updated));
    }
}

public sealed class DemoShippingProvider : IShippingProvider
{
    private readonly ConcurrentDictionary<Guid, Shipment> _shipments = new();
    public string Name => "Demo Shipping";
    public ShippingProviderCapabilities Capabilities => ShippingProviderCapabilities.Rates | ShippingProviderCapabilities.Labels | ShippingProviderCapabilities.Tracking | ShippingProviderCapabilities.Cancellation;

    public Task<IReadOnlyList<ShippingRate>> GetRatesAsync(ShippingRateRequest request, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ShippingRate>>([new(Name, "standard", "Demo standard", new Money("USD", 5m), TimeSpan.FromDays(2))]);

    public Task<Shipment> CreateShipmentAsync(ShipmentRequest request, CancellationToken cancellationToken = default)
    {
        Guid id = Guid.NewGuid();
        Shipment shipment = new(id, Name, request.ServiceCode, ShipmentStatuses.LabelCreated, $"DEMO-{id:N}", ProviderReference: id.ToString("N"));
        _shipments[id] = shipment;
        return Task.FromResult(shipment);
    }

    public Task<Shipment?> GetShipmentAsync(Guid shipmentId, CancellationToken cancellationToken = default) => Task.FromResult(_shipments.GetValueOrDefault(shipmentId));
    public async Task<Shipment> RefreshTrackingAsync(Guid shipmentId, CancellationToken cancellationToken = default)
    {
        Shipment current = await GetShipmentAsync(shipmentId, cancellationToken) ?? throw new KeyNotFoundException();
        Shipment updated = current with { Status = ShipmentStatuses.InTransit };
        _shipments[shipmentId] = updated;
        return updated;
    }

    public async Task CancelShipmentAsync(Guid shipmentId, CancellationToken cancellationToken = default)
    {
        Shipment current = await GetShipmentAsync(shipmentId, cancellationToken) ?? throw new KeyNotFoundException();
        _shipments[shipmentId] = current with { Status = ShipmentStatuses.Cancelled };
    }

    public Task<IReadOnlyList<PickupLocation>> GetPickupLocationsAsync(string countryCode, string? subdivisionCode = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PickupLocation>>([]);
}

public sealed class DemoDiscountProvider : AngryMonkey.CloudCommerce.IDiscountProvider
{
    public Task<IReadOnlyList<AngryMonkey.CloudCommerce.DiscountAdjustment>> CalculateAsync(AngryMonkey.CloudCommerce.Cart cart, Money itemsTotal, CancellationToken cancellationToken = default)
    {
        if (!cart.CouponCodes.Contains("MONKEY20", StringComparer.OrdinalIgnoreCase))
            return Task.FromResult<IReadOnlyList<AngryMonkey.CloudCommerce.DiscountAdjustment>>([]);

        Money amount = new(itemsTotal.Currency, decimal.Round(itemsTotal.Value * .20m, 2));
        IReadOnlyList<AngryMonkey.CloudCommerce.DiscountAdjustment> adjustments =
        [
            new("MONKEY20", "Demo launch offer", amount, AngryMonkey.CloudCommerce.DiscountTypes.Percentage)
        ];
        return Task.FromResult(adjustments);
    }
}

public sealed class DemoCommerceTaxProvider : AngryMonkey.CloudCommerce.ICommerceTaxProvider
{
    public Task<Money> CalculateAsync(AngryMonkey.CloudCommerce.Cart cart, Money taxableAmount, AngryMonkey.CloudCommerce.ShippingSelection? shipping, CancellationToken cancellationToken = default) =>
        Task.FromResult(new Money(taxableAmount.Currency, decimal.Round(taxableAmount.Value * .05m, 2)));
}