using System.Collections.Concurrent;
using AngryMonkey.Cloud.Geography;

namespace AngryMonkey.CloudPayments;

public sealed class PaymentProviderRegistry(IEnumerable<IPaymentProvider> providers) : IPaymentProviderRegistry
{
    private readonly IReadOnlyDictionary<string, IPaymentProvider> _providers = providers.ToDictionary(provider => provider.Name, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<IPaymentProvider> Providers => [.. _providers.Values];

    public IPaymentProvider Get(string name) => _providers.TryGetValue(name, out IPaymentProvider? provider)
        ? provider
        : throw new InvalidOperationException($"Payment provider '{name}' is not registered.");
}

public sealed class InMemoryPaymentStore : IPaymentStore
{
    private readonly ConcurrentDictionary<(string Provider, string Id), Payment> _payments = new();

    public Task SaveAsync(Payment payment, CancellationToken cancellationToken = default)
    {
        _payments[(payment.Provider.ToUpperInvariant(), payment.Id)] = payment;
        return Task.CompletedTask;
    }

    public Task<Payment?> GetAsync(string provider, string paymentId, CancellationToken cancellationToken = default)
    {
        _payments.TryGetValue((provider.ToUpperInvariant(), paymentId), out Payment? payment);
        return Task.FromResult(payment);
    }
}

public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly ConcurrentDictionary<(string Operation, string Key), PaymentResult> _results = new();

    public Task<PaymentResult?> GetAsync(string operation, string key, CancellationToken cancellationToken = default)
    {
        _results.TryGetValue((operation, key), out PaymentResult? result);
        return Task.FromResult(result);
    }

    public Task SaveAsync(string operation, string key, PaymentResult result, CancellationToken cancellationToken = default)
    {
        _results.TryAdd((operation, key), result);
        return Task.CompletedTask;
    }
}

public sealed class PaymentService(IPaymentProviderRegistry providers, IPaymentStore store, IIdempotencyStore idempotency, CloudPaymentsOptions options, ITaxProvider? taxProvider = null) : IPaymentService
{
    public Task<PaymentResult> CreateAsync(PaymentRequest request, CancellationToken cancellationToken = default)
        => ExecutePaymentRequestAsync("create", request, PaymentProviderCapabilities.Create, cancellationToken);

    public Task<PaymentResult> AuthorizeAsync(PaymentRequest request, CancellationToken cancellationToken = default)
        => ExecutePaymentRequestAsync("authorize", request, PaymentProviderCapabilities.Authorize, cancellationToken);

    public Task<PaymentResult> CaptureAsync(CaptureRequest request, CancellationToken cancellationToken = default)
        => ExecuteAsync("capture", request.Provider, request.IdempotencyKey, PaymentProviderCapabilities.Capture, provider => provider.CaptureAsync(request, cancellationToken), cancellationToken);

    public Task<PaymentResult> VoidAsync(VoidPaymentRequest request, CancellationToken cancellationToken = default)
        => ExecuteAsync("void", request.Provider, request.IdempotencyKey, PaymentProviderCapabilities.Void, provider => provider.VoidAsync(request, cancellationToken), cancellationToken);

    public Task<PaymentResult> RefundAsync(RefundRequest request, CancellationToken cancellationToken = default)
        => ExecuteAsync("refund", request.Provider, request.IdempotencyKey, request.IsPartial ? PaymentProviderCapabilities.PartialRefund : PaymentProviderCapabilities.Refund, provider => provider.RefundAsync(request, cancellationToken), cancellationToken);

    public Task<PaymentResult> ChargeRecurringAsync(RecurringPaymentRequest request, CancellationToken cancellationToken = default)
        => ExecuteAsync("recurring", request.Provider, request.IdempotencyKey, PaymentProviderCapabilities.RecurringPayments, provider => provider.ChargeRecurringAsync(request, cancellationToken), cancellationToken);

    /// <summary>
    /// A payment that has finished moving is answered from the store; one still in flight is
    /// re-read from the provider and the store brought up to date.
    ///
    /// The distinction matters for every flow where the customer leaves to pay somewhere else - a
    /// hosted card form, a bank redirect. What the store holds is whatever the payment looked like
    /// when it was created, which for those flows is always "not paid yet"; the customer then pays
    /// out-of-band and the only place that shows is the provider. Answering such a payment from
    /// the store reports it unpaid forever, however long ago it actually succeeded.
    /// </summary>
    public async Task<Payment?> GetAsync(string provider, string paymentId, CancellationToken cancellationToken = default)
    {
        Payment? stored = await store.GetAsync(provider, paymentId, cancellationToken);
        if (stored is not null && IsSettled(stored.Status))
            return stored;

        Payment? current = await providers.Get(provider).GetAsync(paymentId, cancellationToken);
        if (current is null)
            return stored;

        await store.SaveAsync(current, cancellationToken);
        return current;
    }

    public async Task<PaymentStatuses?> GetStatusAsync(string provider, string paymentId, CancellationToken cancellationToken = default)
    {
        Payment? stored = await store.GetAsync(provider, paymentId, cancellationToken);
        if (stored is not null && IsSettled(stored.Status))
            return stored.Status;

        return await providers.Get(provider).GetStatusAsync(paymentId, cancellationToken) ?? stored?.Status;
    }

    /// <summary>
    /// Whether the payment has reached a state the provider will not move it out of on its own.
    /// Refunded states count as settled deliberately: the stored record of a refund describes the
    /// refund, which re-reading the original payment would overwrite with less information.
    /// </summary>
    private static bool IsSettled(PaymentStatuses status)
        => status is PaymentStatuses.Captured or PaymentStatuses.Refunded or PaymentStatuses.PartiallyRefunded
            or PaymentStatuses.Voided or PaymentStatuses.Cancelled or PaymentStatuses.Failed;

    public async Task<PaymentProviderEvent> ProcessWebhookAsync(PaymentWebhookRequest request, CancellationToken cancellationToken = default)
    {
        IPaymentProvider provider = providers.Get(request.Provider);
        EnsureCapability(provider, PaymentProviderCapabilities.Webhooks);

        if (!await provider.ValidateWebhookAsync(request, cancellationToken))
            throw new InvalidOperationException("Payment webhook validation failed.");

        return await provider.ParseWebhookAsync(request, cancellationToken);
    }

    private async Task<PaymentResult> ExecutePaymentRequestAsync(string operation, PaymentRequest request, PaymentProviderCapabilities capability, CancellationToken cancellationToken)
    {
        PaymentRequest effectiveRequest = request;

        if (options.TaxCalculationEnabled)
        {
            if (taxProvider is null)
                throw new InvalidOperationException("Tax calculation is enabled but no tax provider is registered.");

            TaxCalculationResult tax = await taxProvider.CalculateAsync(new(request.Amount, Metadata: request.Metadata), cancellationToken);
            if (!tax.Tax.Currency.Equals(request.Amount.Currency, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Tax and payment amounts must use the same currency.");
            effectiveRequest = new()
            {
                Provider = request.Provider,
                Amount = new Money(request.Amount.Currency, request.Amount.Value + tax.Tax.Value),
                IdempotencyKey = request.IdempotencyKey,
                Description = request.Description,
                CustomerReference = request.CustomerReference,
                PaymentMethod = request.PaymentMethod,
                Metadata = request.Metadata
            };
        }

        return await ExecuteAsync(operation, effectiveRequest.Provider, effectiveRequest.IdempotencyKey, capability, provider => operation == "create" ? provider.CreateAsync(effectiveRequest, cancellationToken) : provider.AuthorizeAsync(effectiveRequest, cancellationToken), cancellationToken);
    }

    private async Task<PaymentResult> ExecuteAsync(string operation, string providerName, string? idempotencyKey, PaymentProviderCapabilities capability, Func<IPaymentProvider, Task<PaymentResult>> execute, CancellationToken cancellationToken)
    {
        string idempotencyOperation = $"{providerName.Trim().ToUpperInvariant()}:{operation}";
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            PaymentResult? existing = await idempotency.GetAsync(idempotencyOperation, idempotencyKey, cancellationToken);
            if (existing is not null)
                return existing;
        }

        IPaymentProvider provider = providers.Get(providerName);
        EnsureCapability(provider, capability);
        PaymentResult result = await execute(provider);

        if (result.Payment is not null)
            await store.SaveAsync(result.Payment, cancellationToken);

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
            await idempotency.SaveAsync(idempotencyOperation, idempotencyKey, result, cancellationToken);

        return result;
    }

    private static void EnsureCapability(IPaymentProvider provider, PaymentProviderCapabilities capability)
    {
        if (!provider.Capabilities.HasFlag(capability))
            throw new NotSupportedException($"Payment provider '{provider.Name}' does not support {capability}.");
    }
}
