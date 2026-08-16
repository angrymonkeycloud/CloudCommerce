namespace AngryMonkey.CloudPayments.PayPal;

public sealed class PayPalOptions
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string WebhookId { get; set; } = string.Empty;
    public Uri ApiBaseAddress { get; set; } = new("https://api-m.sandbox.paypal.com/");

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ClientSecret))
            throw new InvalidOperationException("PayPal ClientId and ClientSecret are required.");
    }
}
