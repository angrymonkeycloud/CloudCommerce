using AngryMonkey.Cloud.Geography;
using AngryMonkey.CloudPayments;

namespace AngryMonkey.CloudCommerce;

public interface ICartStore
{
    Task<Cart?> GetAsync(Guid cartId, CancellationToken cancellationToken = default);
    Task SaveAsync(Cart cart, CancellationToken cancellationToken = default);
}

public interface IOrderStore
{
    Task<Order?> GetAsync(Guid orderId, CancellationToken cancellationToken = default);
    Task SaveAsync(Order order, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> GetForAccountAsync(CommerceAccountReference account, CancellationToken cancellationToken = default);
}

public interface ICheckoutIdempotencyStore
{
    Task<Order?> GetAsync(string key, CancellationToken cancellationToken = default);
    Task SaveAsync(string key, Order order, CancellationToken cancellationToken = default);
}

public interface IProductResolver
{
    Task<ProductPresentation?> ResolveAsync(string id, CancellationToken cancellationToken = default);
}

public interface IDiscountProvider
{
    Task<IReadOnlyList<DiscountAdjustment>> CalculateAsync(Cart cart, Money itemsTotal, CancellationToken cancellationToken = default);
}

public interface ICommerceTaxProvider
{
    Task<Money> CalculateAsync(Cart cart, Money taxableAmount, ShippingSelection? shipping, CancellationToken cancellationToken = default);
}

public interface ICommercePaymentGateway
{
    Task<Payment> PayAsync(Order order, CheckoutRequest request, CancellationToken cancellationToken = default);
    Task CancelAsync(Order order, string paymentReference, string idempotencyKey, CancellationToken cancellationToken = default);
}

public interface ICommerceLogisticsGateway
{
    Task<IReadOnlyList<Guid>> ReserveAsync(Cart cart, ShippingSelection shipping, CancellationToken cancellationToken = default);
    Task ReleaseAsync(IReadOnlyList<Guid> reservationIds, CancellationToken cancellationToken = default);
}

public interface ICommerceBookingGateway
{
    Task ConfirmAsync(IReadOnlyList<Guid> reservationIds, string? paymentReference, CancellationToken cancellationToken = default);
    Task CancelAsync(IReadOnlyList<Guid> reservationIds, CancellationToken cancellationToken = default);
}

public interface ICommerceService
{
    Task<Cart> CreateCartAsync(string currency, CommerceAccountReference? account = null, CancellationToken cancellationToken = default);
    Task<Cart> AddItemAsync(Guid cartId, ProductPresentation product, int quantity = 1, CancellationToken cancellationToken = default);
    Task<Cart> RemoveItemAsync(Guid cartId, Guid cartItemId, CancellationToken cancellationToken = default);
    Task<Cart> ApplyCouponAsync(Guid cartId, string couponCode, CancellationToken cancellationToken = default);
    Task<CommerceTotals> CalculateTotalsAsync(Guid cartId, ShippingSelection? shipping = null, CancellationToken cancellationToken = default);
    Task<Order> CheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken = default);
}
