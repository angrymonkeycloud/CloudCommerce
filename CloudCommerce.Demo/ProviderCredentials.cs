using AngryMonkey.CloudPayments;
using AngryMonkey.CloudPayments.Adyen;
using AngryMonkey.CloudPayments.MyFatoorah;
using AngryMonkey.CloudPayments.PayPal;
using AngryMonkey.CloudPayments.PayTabs;
using AngryMonkey.CloudPayments.SkipCash;
using AngryMonkey.CloudPayments.Stripe;
using AngryMonkey.CloudPayments.Tap;

namespace AngryMonkey.CloudCommerce.Demo;

/// <summary>A single credential a provider adapter needs before it can be constructed.</summary>
public sealed record ProviderCredentialField(string Key, string Label, string Placeholder, bool IsRequired = true, bool IsSecret = true, bool IsNumeric = false)
{
    public string Hint => IsRequired ? "Required" : "Optional";
}

/// <summary>
/// Holds credentials typed into the portal for the current Blazor circuit only.
/// Values stay in server memory for this one browser session: nothing is written to disk,
/// logged, echoed back to the markup, or shared with another visitor.
/// </summary>
public sealed class ProviderCredentialStore
{
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _disconnected = new(StringComparer.OrdinalIgnoreCase);
    public int Revision { get; private set; }
    public bool IsDisconnected(PaymentProviderLabDefinition definition) => _disconnected.Contains(definition.Slug);
    public void Connect(PaymentProviderLabDefinition definition) { _disconnected.Remove(definition.Slug); Revision++; }

    public string Get(string key) => _values.GetValueOrDefault(key, string.Empty);

    public void Set(string key, string? value)
    {
        string trimmed = value?.Trim() ?? string.Empty;
        Revision++;
        if (trimmed.Length == 0)
            _values.Remove(key);
        else
            _values[key] = trimmed;
    }

    public bool HasRequired(PaymentProviderLabDefinition definition)
        => definition.Credentials.Where(field => field.IsRequired).All(field => !string.IsNullOrWhiteSpace(Get(field.Key)));

    public bool HasAny(PaymentProviderLabDefinition definition)
        => definition.Credentials.Any(field => !string.IsNullOrWhiteSpace(Get(field.Key)));

    public void Clear(PaymentProviderLabDefinition definition)
    {
        foreach (ProviderCredentialField field in definition.Credentials)
            _values.Remove(field.Key);
        _disconnected.Add(definition.Slug);
        Revision++;
    }

    /// <summary>Prefills the form from user-secrets/appsettings so an already-configured machine keeps working.</summary>
    public int SeedFrom(IConfiguration configuration, PaymentProviderLabDefinition definition)
    {
        if (IsDisconnected(definition))
            return 0;
        int seeded = 0;
        foreach (ProviderCredentialField field in definition.Credentials)
        {
            if (!string.IsNullOrWhiteSpace(Get(field.Key)))
                continue;

            string? value = configuration[field.Key];
            if (string.IsNullOrWhiteSpace(value))
                continue;

            Set(field.Key, value);
            seeded++;
        }

        return seeded;
    }
}

/// <summary>
/// Builds a real provider adapter from portal-entered credentials, so a lab can run without
/// restarting the application. Falls back to whatever was registered at startup.
/// </summary>
public sealed class RuntimePaymentProviderFactory(IHttpClientFactory httpClientFactory, IPaymentProviderRegistry registry, ProviderCredentialStore store) : IDisposable
{
    private readonly Dictionary<string, IPaymentProvider> _providers = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<HttpClient> _clients = [];
    private int _revision = -1;

    public string? ValidationError(PaymentProviderLabDefinition definition)
    {
        if (store.IsDisconnected(definition))
            return "This provider is disconnected. Add test keys and connect again.";
        foreach (ProviderCredentialField field in definition.Credentials)
        {
            string value = store.Get(field.Key);
            if (field.IsNumeric && value.Length > 0 && (!long.TryParse(value, out long number) || number < 1 || (field.Key == "MyFatoorah:PaymentMethodId" && number > int.MaxValue)))
                return $"{field.Label} must be a positive whole number.";
            if (field.IsSecret && (value.StartsWith("sk_live_", StringComparison.OrdinalIgnoreCase) || value.StartsWith("rk_live_", StringComparison.OrdinalIgnoreCase)))
                return "Live keys are not accepted. Use provider-issued test credentials.";
        }
        if (definition.ProviderName == "Stripe" && store.Get("Stripe:SecretKey") is { Length: > 0 } key && !key.StartsWith("sk_test_", StringComparison.Ordinal) && !key.StartsWith("rk_test_", StringComparison.Ordinal))
            return "Stripe requires an sk_test_ or rk_test_ key.";
        return null;
    }

    /// <summary>The adapter to call for this lab, or null when credentials are still missing.</summary>
    public IPaymentProvider? Resolve(PaymentProviderLabDefinition definition)
    {
        if (!IsReady(definition))
            return null;
        if (_revision != store.Revision)
        {
            Dispose();
            _revision = store.Revision;
        }
        if (!definition.RequiresCredentials || !store.HasRequired(definition))
            return Registered(definition);
        if (_providers.TryGetValue(definition.Slug, out IPaymentProvider? cached))
            return cached;
        IPaymentProvider? provider = Build(definition);
        if (provider is not null)
            _providers[definition.Slug] = provider;
        return provider;
    }

    /// <summary>True when the lab can run right now, from either source.</summary>
    public bool IsReady(PaymentProviderLabDefinition definition)
    {
        if (ValidationError(definition) is not null)
            return false;
        return definition.RequiresCredentials && store.HasRequired(definition) || Registered(definition) is not null;
    }

    /// <summary>True when the running adapter came from credentials typed into the portal.</summary>
    public bool IsUsingPortalCredentials(PaymentProviderLabDefinition definition)
        => definition.RequiresCredentials && store.HasRequired(definition);

    private IPaymentProvider? Registered(PaymentProviderLabDefinition definition)
        => registry.Providers.FirstOrDefault(provider => provider.Name.Equals(definition.ProviderName, StringComparison.OrdinalIgnoreCase));

    private HttpClient Client(Uri baseAddress)
    {
        HttpClient client = httpClientFactory.CreateClient(nameof(RuntimePaymentProviderFactory));
        client.BaseAddress = baseAddress;
        client.Timeout = TimeSpan.FromSeconds(30);
        _clients.Add(client);
        return client;
    }

    public void Dispose()
    {
        foreach (HttpClient client in _clients)
            client.Dispose();
        _clients.Clear();
        _providers.Clear();
    }

    private IPaymentProvider? Build(PaymentProviderLabDefinition definition)
    {
        string Value(string key) => store.Get(key);
        long Number(string key) => long.TryParse(Value(key), out long parsed) ? parsed : 0;

        switch (definition.ProviderName)
        {
            case "Stripe":
            {
                StripeOptions options = new() { SecretKey = Value("Stripe:SecretKey"), WebhookSecret = Value("Stripe:WebhookSecret") };
                return new StripePaymentProvider(Client(options.ApiBaseAddress), options);
            }
            case "PayPal":
            {
                PayPalOptions options = new() { ClientId = Value("PayPal:ClientId"), ClientSecret = Value("PayPal:ClientSecret"), WebhookId = Value("PayPal:WebhookId") };
                return new PayPalPaymentProvider(Client(options.ApiBaseAddress), options);
            }
            case "Adyen":
            {
                AdyenOptions options = new() { ApiKey = Value("Adyen:ApiKey"), MerchantAccount = Value("Adyen:MerchantAccount"), HmacKey = Value("Adyen:HmacKey") };
                return new AdyenPaymentProvider(Client(options.ApiBaseAddress), options);
            }
            case "MyFatoorah":
            {
                MyFatoorahOptions options = new() { ApiToken = Value("MyFatoorah:ApiToken"), WebhookSecret = Value("MyFatoorah:WebhookSecret"), PaymentMethodId = (int)Number("MyFatoorah:PaymentMethodId") };
                return new MyFatoorahPaymentProvider(Client(options.ApiBaseAddress), options);
            }
            case "SkipCash":
            {
                SkipCashOptions options = new() { ClientId = Value("SkipCash:ClientId"), KeyId = Value("SkipCash:KeyId"), KeySecret = Value("SkipCash:KeySecret"), WebhookKey = Value("SkipCash:WebhookKey") };
                return new SkipCashPaymentProvider(Client(options.ApiBaseAddress), options);
            }
            case "Tap":
            {
                TapOptions options = new() { SecretKey = Value("Tap:SecretKey"), MerchantId = Value("Tap:MerchantId") };
                return new TapPaymentProvider(Client(options.ApiBaseAddress), options);
            }
            case "PayTabs":
            {
                PayTabsOptions options = new() { ProfileId = Number("PayTabs:ProfileId"), ServerKey = Value("PayTabs:ServerKey") };
                return new PayTabsPaymentProvider(Client(options.ApiBaseAddress), options);
            }
            default:
                return Registered(definition);
        }
    }
}
