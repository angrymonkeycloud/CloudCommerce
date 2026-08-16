namespace AngryMonkey.CloudPayments.Stripe;

public sealed class StripeOptions
{
    public string SecretKey { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
    public Uri ApiBaseAddress { get; set; } = new("https://api.stripe.com/");
    public TimeSpan WebhookTolerance { get; set; } = TimeSpan.FromMinutes(5);

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(SecretKey))
            throw new InvalidOperationException("Stripe SecretKey is required.");
    }
}
