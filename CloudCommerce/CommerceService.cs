using System.Collections.Concurrent;
using AngryMonkey.Cloud.Geography;
using AngryMonkey.CloudLogistics;
using AngryMonkey.CloudPayments;

namespace AngryMonkey.CloudCommerce;

public sealed class InMemoryCartStore : ICartStore
{
    private readonly ConcurrentDictionary<Guid, Cart> _carts = new();
    public Task<Cart?> GetAsync(Guid cartId, CancellationToken cancellationToken = default) => Task.FromResult(_carts.TryGetValue(cartId, out Cart? cart) ? cart : null);
    public Task SaveAsync(Cart cart, CancellationToken cancellationToken = default) { _carts[cart.Id] = cart; return Task.CompletedTask; }
}

public sealed class InMemoryOrderStore : IOrderStore
{
    private readonly ConcurrentDictionary<Guid, Order> _orders = new();
    public Task<Order?> GetAsync(Guid orderId, CancellationToken cancellationToken = default) => Task.FromResult(_orders.TryGetValue(orderId, out Order? order) ? order : null);
    public Task SaveAsync(Order order, CancellationToken cancellationToken = default) { _orders[order.Id] = order; return Task.CompletedTask; }
    public Task<IReadOnlyList<Order>> GetForAccountAsync(CommerceAccountReference account, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Order> orders = [.. _orders.Values.Where(order => order.Account == account).OrderByDescending(order => order.CreatedAt)];
        return Task.FromResult(orders);
    }
}

public sealed class InMemoryCheckoutIdempotencyStore : ICheckoutIdempotencyStore
{
    private readonly ConcurrentDictionary<string, Order> _orders = new(StringComparer.Ordinal);

    public Task<Order?> GetAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(_orders.GetValueOrDefault(key));

    public Task SaveAsync(string key, Order order, CancellationToken cancellationToken = default)
    {
        _orders.TryAdd(key, order);
        return Task.CompletedTask;
    }
}

public sealed class CommerceService(ICartStore carts, IOrderStore orders, IEnumerable<IDiscountProvider> discounts, ICommerceTaxProvider? taxes = null, ICommercePaymentGateway? payments = null, ICommerceLogisticsGateway? logistics = null, ICommerceBookingGateway? bookings = null, ICheckoutIdempotencyStore? checkoutIdempotency = null) : ICommerceService
{
    public async Task<Cart> CreateCartAsync(string currency, CommerceAccountReference? account = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        Cart cart = new() { Currency = currency.ToUpperInvariant(), Account = account };
        await carts.SaveAsync(cart, cancellationToken);
        return cart;
    }

    public async Task<Cart> AddItemAsync(Guid cartId, ProductPresentation product, int quantity = 1, CancellationToken cancellationToken = default)
    {
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity));

        Cart cart = await GetActiveCartAsync(cartId, cancellationToken);
        if (!cart.Currency.Equals(product.Price.Currency, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cart items must use the cart currency.");

        List<CartItem> items = [.. cart.Items];
        CartItem? existing = items.FirstOrDefault(item => item.Product.Id == product.Id && item.Product.Reference == product.Reference);
        if (existing is null)
            items.Add(new() { Product = product, Quantity = quantity });
        else
        {
            items.Remove(existing);
            items.Add(new() { Id = existing.Id, Product = product, Quantity = existing.Quantity + quantity, Metadata = existing.Metadata });
        }

        Cart updated = CopyCart(cart, items: items);
        await carts.SaveAsync(updated, cancellationToken);
        return updated;
    }

    public async Task<Cart> RemoveItemAsync(Guid cartId, Guid cartItemId, CancellationToken cancellationToken = default)
    {
        Cart cart = await GetActiveCartAsync(cartId, cancellationToken);
        Cart updated = CopyCart(cart, items: [.. cart.Items.Where(item => item.Id != cartItemId)]);
        await carts.SaveAsync(updated, cancellationToken);
        return updated;
    }

    public async Task<Cart> ApplyCouponAsync(Guid cartId, string couponCode, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(couponCode);
        Cart cart = await GetActiveCartAsync(cartId, cancellationToken);
        IReadOnlyList<string> codes = cart.CouponCodes.Contains(couponCode, StringComparer.OrdinalIgnoreCase) ? cart.CouponCodes : [.. cart.CouponCodes, couponCode.Trim()];
        Cart updated = CopyCart(cart, couponCodes: codes);
        await carts.SaveAsync(updated, cancellationToken);
        return updated;
    }

    public async Task<CommerceTotals> CalculateTotalsAsync(Guid cartId, ShippingSelection? shipping = null, CancellationToken cancellationToken = default)
    {
        Cart cart = await GetActiveCartAsync(cartId, cancellationToken);
        Money items = new(cart.Currency, cart.Items.Sum(item => item.Product.Price.Value * item.Quantity));
        EnsureCurrency(cart.Currency, shipping?.Price);
        List<DiscountAdjustment> adjustments = [];
        foreach (IDiscountProvider discount in discounts)
            adjustments.AddRange(await discount.CalculateAsync(cart, items, cancellationToken));

        foreach (DiscountAdjustment adjustment in adjustments)
            EnsureCurrency(cart.Currency, adjustment.Amount);

        decimal discountValue = Math.Min(items.Value, adjustments.Sum(adjustment => Math.Abs(adjustment.Amount.Value)));
        Money discountTotal = new(cart.Currency, discountValue);
        Money shippingTotal = shipping?.Price ?? new(cart.Currency, 0m);
        Money taxable = new(cart.Currency, items.Value - discountValue + shippingTotal.Value);
        Money tax = taxes is null ? new(cart.Currency, 0m) : await taxes.CalculateAsync(cart, taxable, shipping, cancellationToken);
        EnsureCurrency(cart.Currency, tax);
        Money final = new(cart.Currency, taxable.Value + tax.Value);

        return new() { Items = items, Discount = discountTotal, Shipping = shippingTotal, Tax = tax, Final = final, Adjustments = adjustments };
    }

    public async Task<Order> CheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.IdempotencyKey);
        Order? existingOrder = checkoutIdempotency is null ? null : await checkoutIdempotency.GetAsync(request.IdempotencyKey, cancellationToken);
        if (existingOrder is not null)
            return existingOrder;

        Cart cart = await GetActiveCartAsync(request.CartId, cancellationToken);
        if (cart.Items.Count == 0)
            throw new InvalidOperationException("An empty cart cannot be checked out.");

        CommerceTotals totals = await CalculateTotalsAsync(cart.Id, request.Shipping, cancellationToken);
        List<Guid> reservationIds = [];

        if (request.Shipping is not null && logistics is not null)
            reservationIds.AddRange(await logistics.ReserveAsync(cart, request.Shipping, cancellationToken));

        Order pending = new()
        {
            CartId = cart.Id,
            Account = request.Account ?? cart.Account,
            Items = [.. cart.Items.Select(item => new OrderItem(Guid.NewGuid(), item.Product, item.Quantity, item.Product.Price, item.Metadata))],
            Totals = totals,
            Status = payments is null ? OrderStatuses.Pending : OrderStatuses.AwaitingPayment,
            PaymentProvider = request.PaymentProvider,
            InventoryReservationIds = reservationIds,
            Shipping = request.Shipping,
            BookingReservationIds = request.BookingReservationIds,
            Metadata = request.Metadata
        };

        Payment? payment = null;
        try
        {
            payment = payments is null ? null : await payments.PayAsync(pending, request, cancellationToken);
            OrderStatuses orderStatus = payment?.Status switch
            {
                PaymentStatuses.Captured or PaymentStatuses.PartiallyRefunded or PaymentStatuses.Refunded => OrderStatuses.Paid,
                PaymentStatuses.Authorized or PaymentStatuses.Pending or PaymentStatuses.RequiresAction => OrderStatuses.AwaitingPayment,
                _ when payments is null => OrderStatuses.Pending,
                _ => throw new InvalidOperationException($"Payment ended in unsupported status '{payment?.Status}'.")
            };
            Order completed = CopyOrder(pending, orderStatus, payment);
            if (bookings is not null && request.BookingReservationIds.Count > 0 && payment?.Status is not PaymentStatuses.Pending and not PaymentStatuses.RequiresAction)
                await bookings.ConfirmAsync(request.BookingReservationIds, payment?.Id, cancellationToken);
            await orders.SaveAsync(completed, cancellationToken);
            await carts.SaveAsync(CopyCart(cart, status: CartStatuses.Converted), cancellationToken);
            if (checkoutIdempotency is not null)
                await checkoutIdempotency.SaveAsync(request.IdempotencyKey, completed, cancellationToken);
            return completed;
        }
        catch (Exception checkoutException)
        {
            List<Exception> compensationErrors = [checkoutException];

            try
            {
                if (logistics is not null && reservationIds.Count > 0)
                    await logistics.ReleaseAsync(reservationIds, cancellationToken);
            }
            catch (Exception exception)
            {
                compensationErrors.Add(exception);
            }

            try
            {
                if (bookings is not null && request.BookingReservationIds.Count > 0)
                    await bookings.CancelAsync(request.BookingReservationIds, cancellationToken);
            }
            catch (Exception exception)
            {
                compensationErrors.Add(exception);
            }

            try
            {
                if (payments is not null && payment is not null)
                    await payments.CancelAsync(pending, payment.Id, $"{request.IdempotencyKey}:compensate", cancellationToken);
            }
            catch (Exception exception)
            {
                compensationErrors.Add(exception);
            }

            if (compensationErrors.Count > 1)
                throw new AggregateException("Checkout failed and one or more compensating operations also failed.", compensationErrors);

            throw;
        }
    }

    private async Task<Cart> GetActiveCartAsync(Guid cartId, CancellationToken cancellationToken)
    {
        Cart cart = await carts.GetAsync(cartId, cancellationToken) ?? throw new KeyNotFoundException($"Cart '{cartId}' was not found.");
        return cart.Status == CartStatuses.Active ? cart : throw new InvalidOperationException("Only active carts can be changed.");
    }

    private static void EnsureCurrency(string currency, Money? amount)
    {
        if (amount is not null && !amount.Currency.Equals(currency, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Expected {currency} but received {amount.Currency}.");
    }

    private static Cart CopyCart(Cart cart, IReadOnlyList<CartItem>? items = null, IReadOnlyList<string>? couponCodes = null, CartStatuses? status = null) => new()
    {
        Id = cart.Id,
        Account = cart.Account,
        Currency = cart.Currency,
        Status = status ?? cart.Status,
        Items = items ?? cart.Items,
        CouponCodes = couponCodes ?? cart.CouponCodes,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static Order CopyOrder(Order order, OrderStatuses status, Payment? payment) => new()
    {
        Id = order.Id,
        CartId = order.CartId,
        Account = order.Account,
        Items = order.Items,
        Totals = order.Totals,
        Status = status,
        PaymentProvider = order.PaymentProvider,
        PaymentReference = payment?.Id,
        PaymentStatus = payment?.Status,
        PaymentAction = payment?.NextAction,
        InventoryReservationIds = order.InventoryReservationIds,
        Shipping = order.Shipping,
        BookingReservationIds = order.BookingReservationIds,
        CreatedAt = order.CreatedAt,
        Metadata = order.Metadata
    };
}

public sealed class CloudPaymentsCommerceGateway(IPaymentService payments) : ICommercePaymentGateway
{
    public async Task<Payment> PayAsync(Order order, CheckoutRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.PaymentProvider))
            throw new InvalidOperationException("A payment provider is required for payment orchestration.");

        PaymentResult result = await payments.CreateAsync(new()
        {
            Provider = request.PaymentProvider,
            Amount = order.Totals.Final,
            IdempotencyKey = request.IdempotencyKey,
            CustomerReference = request.Account?.OrganizationId?.ToString() ?? request.Account?.UserId?.ToString(),
            PaymentMethod = request.PaymentMethod,
            Description = $"Order {order.Id}",
            Metadata = new(request.Metadata) { ["orderId"] = order.Id.ToString() }
        }, cancellationToken);

        if (!result.IsSuccessful)
            throw new InvalidOperationException(result.Error?.Message ?? "Payment failed.");

        return result.Payment!;
    }

    public async Task CancelAsync(Order order, string paymentReference, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(order.PaymentProvider))
            throw new InvalidOperationException("The order has no payment provider.");

        PaymentStatuses? status = await payments.GetStatusAsync(order.PaymentProvider, paymentReference, cancellationToken);
        PaymentResult result = status == PaymentStatuses.Authorized
            ? await payments.VoidAsync(new(order.PaymentProvider, paymentReference, idempotencyKey), cancellationToken)
            : await payments.RefundAsync(new(order.PaymentProvider, paymentReference, order.Totals.Final, idempotencyKey), cancellationToken);

        if (!result.IsSuccessful)
            throw new InvalidOperationException(result.Error?.Message ?? "Payment compensation failed.");
    }
}

public sealed class CloudLogisticsCommerceGateway(IInventoryService inventory) : ICommerceLogisticsGateway
{
    public async Task<IReadOnlyList<Guid>> ReserveAsync(Cart cart, ShippingSelection shipping, CancellationToken cancellationToken = default)
    {
        List<Guid> ids = [];
        foreach (CartItem item in cart.Items)
        {
            InventoryReservation reservation = await inventory.ReserveAsync(new(item.Product.Reference ?? item.Product.Id), shipping.WarehouseId, item.Quantity, TimeSpan.FromMinutes(15), cart.Id.ToString(), cancellationToken);
            ids.Add(reservation.Id);
        }
        return ids;
    }

    public async Task ReleaseAsync(IReadOnlyList<Guid> reservationIds, CancellationToken cancellationToken = default)
    {
        foreach (Guid reservationId in reservationIds)
            await inventory.ReleaseAsync(reservationId, cancellationToken);
    }
}
