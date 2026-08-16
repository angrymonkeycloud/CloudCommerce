using System.Text.Json;
using AngryMonkey.CloudCommerce.Components;
using AngryMonkey.CloudPayments;

namespace AngryMonkey.CloudCommerce.Demo;

public enum DemoPaymentStages
{
    Ready,
    Created,
    RequiresAction,
    Authorized,
    Captured,
    Refunded
}

public sealed class PaymentFlowSimulator
{
    public ProviderCardModel Provider { get; private set; } = DemoCatalog.PaymentProviders[3];
    public DemoPaymentStages Stage { get; private set; }
    public decimal Amount { get; private set; } = 114m;
    public string Currency { get; private set; } = "USD";
    public string Reference { get; private set; } = "sim_pending";

    public void Select(ProviderCardModel provider)
    {
        Provider = provider;
        Reset();
    }

    public void Configure(decimal amount, string currency)
    {
        Amount = Math.Max(.01m, amount);
        Currency = currency.Trim().ToUpperInvariant();
    }

    public void Advance()
    {
        Stage = Stage switch
        {
            DemoPaymentStages.Ready => DemoPaymentStages.Created,
            DemoPaymentStages.Created => DemoPaymentStages.RequiresAction,
            DemoPaymentStages.RequiresAction => DemoPaymentStages.Authorized,
            DemoPaymentStages.Authorized => DemoPaymentStages.Captured,
            DemoPaymentStages.Captured => DemoPaymentStages.Refunded,
            _ => DemoPaymentStages.Ready
        };
        Reference = $"sim_{Provider.Name.Replace(" ", string.Empty).ToLowerInvariant()}_{Stage.ToString().ToLowerInvariant()}";
    }

    public void Reset()
    {
        Stage = DemoPaymentStages.Ready;
        Reference = "sim_pending";
    }

    public string ActionLabel => Stage switch
    {
        DemoPaymentStages.Ready => "Create payment",
        DemoPaymentStages.Created => "Simulate customer action",
        DemoPaymentStages.RequiresAction => "Authorize payment",
        DemoPaymentStages.Authorized => "Capture payment",
        DemoPaymentStages.Captured => "Issue refund",
        _ => "Run again"
    };

    public string Json => JsonSerializer.Serialize(new
    {
        provider = Provider.Name,
        amount = new { value = Amount, currency = Currency },
        status = Stage,
        reference = Reference,
        externalCall = false,
        mode = "safe-simulation"
    }, new JsonSerializerOptions { WriteIndented = true });
}