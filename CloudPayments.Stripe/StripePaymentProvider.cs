using System.Globalization;
using System.Text;
using System.Text.Json;
using AngryMonkey.Cloud.Geography;
using Stripe;
using Money = AngryMonkey.Cloud.Geography.Money;

namespace AngryMonkey.CloudPayments.Stripe;

/// <summary>
/// Stripe behind the CloudPayments abstraction, on Stripe's own SDK (Stripe.net).
///
/// The SDK earns its place by owning the three things a hand-written client keeps getting wrong:
/// it pins the API version it was generated against, so an account's Dashboard default can never
/// reshape a response; it models the request and response bodies, so a renamed or nested field is
/// a compile error instead of a silent null; and it turns API failures into typed StripeException
/// with the error payload already parsed. What stays here is only the mapping between Stripe's
/// vocabulary and this library's.
/// </summary>
public sealed class StripePaymentProvider(HttpClient httpClient, StripeOptions options) : IPaymentProvider
{
    private static readonly HashSet<string> ZeroDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase) { "BIF", "CLP", "DJF", "GNF", "JPY", "KMF", "KRW", "MGA", "PYG", "RWF", "UGX", "VND", "VUV", "XAF", "XOF", "XPF" };

    private StripeClient? _client;

    public string Name => "Stripe";

    public PaymentProviderCapabilities Capabilities => PaymentProviderCapabilities.Create | PaymentProviderCapabilities.Authorize | PaymentProviderCapabilities.Capture | PaymentProviderCapabilities.Void | PaymentProviderCapabilities.Refund | PaymentProviderCapabilities.PartialRefund | PaymentProviderCapabilities.SavedPaymentMethods | PaymentProviderCapabilities.RecurringPayments | PaymentProviderCapabilities.Webhooks;

    /// <summary>
    /// Created on first use rather than in the constructor: a host may build the provider before
    /// it has a secret (the demo's credential lab does), and only an actual request should fail
    /// for that. Stripe.net addresses the API with absolute URLs, so the configured base has to
    /// reach it through ApiBase - setting HttpClient.BaseAddress alone would be ignored.
    /// </summary>
    private StripeClient Client
    {
        get
        {
            options.Validate();
            return _client ??= new StripeClient(new StripeClientOptions
            {
                ApiKey = options.SecretKey,
                ApiBase = options.ApiBaseAddress.ToString().TrimEnd('/'),
                HttpClient = new SystemNetHttpClient(httpClient)
            });
        }
    }

    public Task<PaymentResult> CreateAsync(PaymentRequest request, CancellationToken cancellationToken = default)
        => CreateIntentAsync(request, false, false, cancellationToken);

    public Task<PaymentResult> AuthorizeAsync(PaymentRequest request, CancellationToken cancellationToken = default)
        => CreateIntentAsync(request, true, false, cancellationToken);

    public Task<PaymentResult> CaptureAsync(CaptureRequest request, CancellationToken cancellationToken = default)
    {
        PaymentIntentCaptureOptions captureOptions = new();
        if (request.Amount is not null)
            captureOptions.AmountToCapture = ToMinorUnits(request.Amount);

        return ExecuteAsync(() => Client.V1.PaymentIntents.CaptureAsync(request.PaymentId, captureOptions, RequestFor(request.IdempotencyKey), cancellationToken), MapPayment);
    }

    public Task<PaymentResult> VoidAsync(VoidPaymentRequest request, CancellationToken cancellationToken = default)
        => ExecuteAsync(() => Client.V1.PaymentIntents.CancelAsync(request.PaymentId, new PaymentIntentCancelOptions(), RequestFor(request.IdempotencyKey), cancellationToken), MapPayment);

    /// <summary>
    /// A refund is its own Stripe object, but this library reports refunds as a new state of the
    /// payment they came from - so the original payment id stays the identity and the refund id
    /// travels as the provider reference.
    /// </summary>
    public Task<PaymentResult> RefundAsync(RefundRequest request, CancellationToken cancellationToken = default)
    {
        RefundCreateOptions refundOptions = new()
        {
            PaymentIntent = request.PaymentId,
            Amount = ToMinorUnits(request.Amount)
        };
        if (request.Reason is "duplicate" or "fraudulent" or "requested_by_customer")
            refundOptions.Reason = request.Reason;

        return ExecuteAsync(
            () => Client.V1.Refunds.CreateAsync(refundOptions, RequestFor(request.IdempotencyKey), cancellationToken),
            refund => new Payment
            {
                Id = request.PaymentId,
                Provider = Name,
                ProviderReference = refund.Id,
                Amount = request.Amount,
                Status = request.IsPartial ? PaymentStatuses.PartiallyRefunded : PaymentStatuses.Refunded
            });
    }

    public Task<PaymentResult> ChargeRecurringAsync(RecurringPaymentRequest request, CancellationToken cancellationToken = default)
        => CreateIntentAsync(new PaymentRequest
        {
            Provider = Name,
            Amount = request.Amount,
            IdempotencyKey = request.IdempotencyKey,
            CustomerReference = request.CustomerReference,
            PaymentMethod = request.PaymentMethod,
            Metadata = request.Metadata ?? []
        }, false, true, cancellationToken);

    public async Task<Payment?> GetAsync(string paymentId, CancellationToken cancellationToken = default)
    {
        try
        {
            return MapPayment(await Client.V1.PaymentIntents.GetAsync(paymentId, null, null, cancellationToken));
        }
        catch (StripeException)
        {
            return null;
        }
    }

    public async Task<PaymentStatuses?> GetStatusAsync(string paymentId, CancellationToken cancellationToken = default)
        => (await GetAsync(paymentId, cancellationToken))?.Status;

    public Task<bool> ValidateWebhookAsync(PaymentWebhookRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(options.WebhookSecret) || !TryGetHeader(request.Headers, "Stripe-Signature", out string? signatureHeader))
            return Task.FromResult(false);

        try
        {
            // The SDK's own constant-time comparison and replay window - a wrong signature, a
            // malformed header and a timestamp outside the tolerance all arrive here as the same
            // StripeException, and all mean the same thing to a caller: don't trust this body.
            EventUtility.ValidateSignature(
                Encoding.UTF8.GetString(request.Body.Span),
                signatureHeader!,
                options.WebhookSecret,
                (long)options.WebhookTolerance.TotalSeconds);
            return Task.FromResult(true);
        }
        catch (StripeException)
        {
            return Task.FromResult(false);
        }
    }

    public Task<PaymentProviderEvent> ParseWebhookAsync(PaymentWebhookRequest request, CancellationToken cancellationToken = default)
    {
        string json = Encoding.UTF8.GetString(request.Body.Span);

        // Version mismatches are not fatal here. A Stripe account's webhook endpoints are pinned
        // to whatever version the Dashboard gave them, routinely older than the SDK's own - and
        // refusing to read those events would drop real settled payments over a cosmetic gap.
        Event stripeEvent = EventUtility.ConstructEventWithoutVerification(json);

        // The entity travels on as raw JSON: it can be any Stripe object and callers match their
        // own fields on it. Taken from the body rather than re-serialised from the typed model,
        // so nothing the SDK doesn't happen to map gets silently dropped in transit.
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement entity = document.RootElement.GetProperty("data").GetProperty("object");

        string? paymentId = (stripeEvent.Data.Object as IHasId)?.Id
            ?? (entity.TryGetProperty("id", out JsonElement id) ? id.GetString() : null);

        return Task.FromResult(new PaymentProviderEvent(
            Name,
            stripeEvent.Id,
            stripeEvent.Type,
            paymentId,
            ToOffset(stripeEvent.Created),
            new Dictionary<string, string> { ["object"] = entity.GetRawText() }));
    }

    private Task<PaymentResult> CreateIntentAsync(PaymentRequest request, bool manualCapture, bool offSession, CancellationToken cancellationToken)
    {
        PaymentIntentCreateOptions intentOptions = new()
        {
            Amount = ToMinorUnits(request.Amount),
            Currency = request.Amount.Currency.ToLowerInvariant(),
            CaptureMethod = manualCapture ? "manual" : "automatic",
            Metadata = new Dictionary<string, string>(request.Metadata)
        };

        if (!string.IsNullOrWhiteSpace(request.Description))
            intentOptions.Description = request.Description;

        // Stripe's own customer id, when the caller has one. Anything else - an application user
        // id, an email - is not a Stripe customer and the API rejects the whole request for it.
        if (!string.IsNullOrWhiteSpace(request.CustomerReference))
            intentOptions.Customer = request.CustomerReference;

        // With a payment method already in hand there is nothing left for the customer to do, so
        // the intent is confirmed in the same call. Without one, the intent is created unconfirmed
        // and its client secret is what lets the browser collect the details.
        if (request.PaymentMethod is not null)
        {
            intentOptions.PaymentMethod = request.PaymentMethod.Reference;
            intentOptions.Confirm = true;
        }

        if (offSession)
            intentOptions.OffSession = true;

        if (request.SavePaymentMethod)
            intentOptions.SetupFutureUsage = "off_session";

        return ExecuteAsync(async () =>
        {
            if (request.SavePaymentMethod && string.IsNullOrWhiteSpace(intentOptions.Customer))
            {
                request.Metadata.TryGetValue("payerEmail", out string? payerEmail);
                request.Metadata.TryGetValue("payerName", out string? payerName);
                Customer customer = await Client.V1.Customers.CreateAsync(
                    new CustomerCreateOptions
                    {
                        Email = payerEmail,
                        Name = payerName,
                        Metadata = new Dictionary<string, string>(request.Metadata)
                    },
                    RequestFor($"{request.IdempotencyKey}-customer"),
                    cancellationToken);
                intentOptions.Customer = customer.Id;
            }

            return await Client.V1.PaymentIntents.CreateAsync(intentOptions, RequestFor(request.IdempotencyKey), cancellationToken);
        }, MapPayment);
    }

    private Payment MapPayment(PaymentIntent intent)
    {
        string currency = (intent.Currency ?? "usd").ToUpperInvariant();

        Uri? redirect = null;
        if (intent.NextAction?.RedirectToUrl?.Url is string redirectUrl)
            Uri.TryCreate(redirectUrl, UriKind.Absolute, out redirect);

        // A freshly created intent - no payment method attached, not confirmed - comes back
        // "requires_payment_method", not "requires_action": that one only appears once a confirmed
        // payment method additionally demands 3DS. Gating on "requires_action" alone left the
        // ordinary create-then-collect-in-the-browser flow with no client secret at all, which is
        // the flow every card payment here uses.
        bool clientCanContinue = intent.Status is "requires_action" or "requires_confirmation" or "requires_payment_method";
        PaymentAction? action = redirect is not null
            ? new(PaymentActionTypes.Redirect, redirect, intent.ClientSecret)
            : clientCanContinue && intent.ClientSecret is not null
                ? new(PaymentActionTypes.ClientSecret, ClientSecret: intent.ClientSecret)
                : null;

        return new()
        {
            Id = intent.Id,
            Provider = Name,
            ProviderReference = intent.Id,
            Amount = new(currency, FromMinorUnits(currency, intent.Amount)),
            Status = intent.Status switch
            {
                "requires_action" or "requires_confirmation" or "requires_payment_method" => PaymentStatuses.RequiresAction,
                "requires_capture" => PaymentStatuses.Authorized,
                "succeeded" => PaymentStatuses.Captured,
                "canceled" => PaymentStatuses.Cancelled,
                "processing" => PaymentStatuses.Pending,
                _ => PaymentStatuses.Pending
            },
            CustomerReference = intent.CustomerId,
            PaymentMethod = string.IsNullOrEmpty(intent.PaymentMethodId)
                ? null
                : new PaymentMethodReference(Name, intent.PaymentMethodId),
            NextAction = action,
            // Carried back out because it is the only thing tying a Stripe payment to the caller's
            // own records once the customer returns from the browser: the metadata sent at create
            // is how a settled intent is matched to the order it was for.
            Metadata = intent.Metadata is null ? [] : new(intent.Metadata),
            CreatedAt = ToOffset(intent.Created)
        };
    }

    private static async Task<PaymentResult> ExecuteAsync<T>(Func<Task<T>> send, Func<T, Payment> map)
    {
        try
        {
            return new(map(await send()));
        }
        catch (StripeException exception)
        {
            return PaymentResult.Failed(MapError(exception));
        }
    }

    private static RequestOptions? RequestFor(string? idempotencyKey)
        => string.IsNullOrWhiteSpace(idempotencyKey) ? null : new RequestOptions { IdempotencyKey = idempotencyKey };

    private static PaymentError MapError(StripeException exception)
    {
        StripeError? error = exception.StripeError;
        int statusCode = (int)exception.HttpStatusCode;
        string code = error?.Code ?? error?.Type ?? statusCode.ToString(CultureInfo.InvariantCulture);
        string message = error?.Message ?? exception.Message;

        PaymentErrorTypes type = statusCode switch
        {
            // Nothing reached Stripe at all - a connection or DNS failure the SDK exhausted its
            // retries on. Worth another attempt later, unlike a request Stripe actually refused.
            0 => PaymentErrorTypes.ProviderUnavailable,
            401 or 403 => PaymentErrorTypes.Authentication,
            429 => PaymentErrorTypes.RateLimited,
            >= 500 => PaymentErrorTypes.ProviderUnavailable,
            _ => error?.DeclineCode is not null || code.Contains("declin", StringComparison.OrdinalIgnoreCase)
                ? PaymentErrorTypes.Declined
                : PaymentErrorTypes.InvalidRequest
        };

        return new(type, code, message, code, statusCode is 0 or 409 or 429 or >= 500);
    }

    private static DateTimeOffset ToOffset(DateTime value)
        => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static long ToMinorUnits(Money money)
    {
        decimal multiplier = ZeroDecimalCurrencies.Contains(money.Currency) ? 1m : 100m;
        return checked((long)decimal.Round(money.Value * multiplier, 0, MidpointRounding.AwayFromZero));
    }

    private static decimal FromMinorUnits(string currency, long amount) => ZeroDecimalCurrencies.Contains(currency) ? amount : amount / 100m;

    private static bool TryGetHeader(IReadOnlyDictionary<string, string> headers, string name, out string? value)
    {
        KeyValuePair<string, string> header = headers.FirstOrDefault(pair => pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase));
        value = header.Value;
        return value is not null;
    }
}
