namespace AngryMonkey.CloudPayments;

public interface IPaymentProvider
{
    string Name { get; }
    PaymentProviderCapabilities Capabilities { get; }
    Task<PaymentResult> CreateAsync(PaymentRequest request, CancellationToken cancellationToken = default);
    Task<PaymentResult> AuthorizeAsync(PaymentRequest request, CancellationToken cancellationToken = default);
    Task<PaymentResult> CaptureAsync(CaptureRequest request, CancellationToken cancellationToken = default);
    Task<PaymentResult> VoidAsync(VoidPaymentRequest request, CancellationToken cancellationToken = default);
    Task<PaymentResult> RefundAsync(RefundRequest request, CancellationToken cancellationToken = default);
    Task<PaymentResult> ChargeRecurringAsync(RecurringPaymentRequest request, CancellationToken cancellationToken = default);
    Task<Payment?> GetAsync(string paymentId, CancellationToken cancellationToken = default);
    Task<PaymentStatuses?> GetStatusAsync(string paymentId, CancellationToken cancellationToken = default);
    Task<bool> ValidateWebhookAsync(PaymentWebhookRequest request, CancellationToken cancellationToken = default);
    Task<PaymentProviderEvent> ParseWebhookAsync(PaymentWebhookRequest request, CancellationToken cancellationToken = default);
}

public interface IPaymentProviderRegistry
{
    IReadOnlyCollection<IPaymentProvider> Providers { get; }
    IPaymentProvider Get(string name);
}

public interface ITaxProvider
{
    Task<TaxCalculationResult> CalculateAsync(TaxCalculationRequest request, CancellationToken cancellationToken = default);
}

public interface IPaymentStore
{
    Task SaveAsync(Payment payment, CancellationToken cancellationToken = default);
    Task<Payment?> GetAsync(string provider, string paymentId, CancellationToken cancellationToken = default);
}

public interface IIdempotencyStore
{
    Task<PaymentResult?> GetAsync(string operation, string key, CancellationToken cancellationToken = default);
    Task SaveAsync(string operation, string key, PaymentResult result, CancellationToken cancellationToken = default);
}

public interface IPaymentService
{
    Task<PaymentResult> CreateAsync(PaymentRequest request, CancellationToken cancellationToken = default);
    Task<PaymentResult> AuthorizeAsync(PaymentRequest request, CancellationToken cancellationToken = default);
    Task<PaymentResult> CaptureAsync(CaptureRequest request, CancellationToken cancellationToken = default);
    Task<PaymentResult> VoidAsync(VoidPaymentRequest request, CancellationToken cancellationToken = default);
    Task<PaymentResult> RefundAsync(RefundRequest request, CancellationToken cancellationToken = default);
    Task<PaymentResult> ChargeRecurringAsync(RecurringPaymentRequest request, CancellationToken cancellationToken = default);
    Task<Payment?> GetAsync(string provider, string paymentId, CancellationToken cancellationToken = default);
    Task<PaymentStatuses?> GetStatusAsync(string provider, string paymentId, CancellationToken cancellationToken = default);
    Task<PaymentProviderEvent> ProcessWebhookAsync(PaymentWebhookRequest request, CancellationToken cancellationToken = default);
}
