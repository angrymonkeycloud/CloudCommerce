using AngryMonkey.Cloud.Geography;

namespace AngryMonkey.CloudPayments;

[Flags]
public enum PaymentProviderCapabilities
{
    None = 0,
    Create = 1,
    Authorize = 2,
    Capture = 4,
    Void = 8,
    Refund = 16,
    PartialRefund = 32,
    SavedPaymentMethods = 64,
    RecurringPayments = 128,
    Webhooks = 256
}

public enum PaymentStatuses
{
    Pending,
    RequiresAction,
    Authorized,
    Captured,
    PartiallyRefunded,
    Refunded,
    Voided,
    Failed,
    Cancelled
}

public enum PaymentMethodTypes
{
    Unknown,
    Card,
    BankAccount,
    Wallet,
    ProviderSpecific
}

public enum PaymentErrorTypes
{
    Unknown,
    InvalidRequest,
    Authentication,
    Declined,
    RateLimited,
    ProviderUnavailable,
    Duplicate,
    Unsupported,
    WebhookValidation
}

public enum PaymentActionTypes
{
    Redirect,
    ClientSecret,
    ProviderInstructions
}

public sealed record PaymentAction(PaymentActionTypes Type, Uri? Url = null, string? ClientSecret = null, IReadOnlyDictionary<string, string>? Data = null);

public sealed record PaymentMethodReference(string Provider, string Reference, PaymentMethodTypes Type = PaymentMethodTypes.ProviderSpecific, string? DisplayName = null);

public sealed record PaymentError(PaymentErrorTypes Type, string Code, string Message, string? ProviderCode = null, bool IsRetryable = false);

public sealed class Payment
{
    public required string Id { get; init; }
    public required string Provider { get; init; }
    public required Money Amount { get; init; }
    public required PaymentStatuses Status { get; init; }
    public string? ProviderReference { get; init; }
    public string? CustomerReference { get; init; }
    public PaymentMethodReference? PaymentMethod { get; init; }
    public PaymentAction? NextAction { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
    public Dictionary<string, string> Metadata { get; init; } = [];
}

public sealed class PaymentRequest
{
    public required string Provider { get; init; }
    public required Money Amount { get; init; }
    public required string IdempotencyKey { get; init; }
    public string? Description { get; init; }
    public string? CustomerReference { get; init; }
    public PaymentMethodReference? PaymentMethod { get; init; }
    /// <summary>
    /// Explicit customer consent to let the provider retain the payment method for later
    /// off-session charges. False is deliberately the default.
    /// </summary>
    public bool SavePaymentMethod { get; init; }
    public PaymentAction? NextAction { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = [];
}

public sealed record CaptureRequest(string Provider, string PaymentId, Money? Amount = null, string? IdempotencyKey = null);
public sealed record VoidPaymentRequest(string Provider, string PaymentId, string? IdempotencyKey = null);
public sealed record RefundRequest(string Provider, string PaymentId, Money Amount, string IdempotencyKey, string? Reason = null, bool IsPartial = false);
public sealed record RecurringPaymentRequest(string Provider, Money Amount, PaymentMethodReference PaymentMethod, string IdempotencyKey, string? CustomerReference = null, Dictionary<string, string>? Metadata = null);

public sealed record PaymentResult(Payment? Payment, PaymentError? Error = null)
{
    public bool IsSuccessful => Payment is not null && Error is null;

    public static PaymentResult Failed(PaymentError error) => new(null, error);
}

public sealed record TaxCalculationRequest(Money Amount, string? CustomerCountryCode = null, string? CustomerSubdivisionCode = null, Dictionary<string, string>? Metadata = null);
public sealed record TaxCalculationResult(Money Tax, string? ProviderReference = null);

public sealed record PaymentWebhookRequest(string Provider, IReadOnlyDictionary<string, string> Headers, ReadOnlyMemory<byte> Body);
public sealed record PaymentProviderEvent(string Provider, string EventId, string EventType, string? PaymentId, DateTimeOffset OccurredAt, IReadOnlyDictionary<string, string> Data);

public sealed class CloudPaymentsOptions
{
    public bool TaxCalculationEnabled { get; set; }
}
