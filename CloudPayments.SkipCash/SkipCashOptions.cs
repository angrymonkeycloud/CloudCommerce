namespace AngryMonkey.CloudPayments.SkipCash;

public sealed class SkipCashOptions
{
    public string ClientId { get; set; } = string.Empty;
    public string KeyId { get; set; } = string.Empty;
    public string KeySecret { get; set; } = string.Empty;
    public string WebhookKey { get; set; } = string.Empty;
    public Uri ApiBaseAddress { get; set; } = new("https://skipcashtest.azurewebsites.net/");

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(KeyId) || string.IsNullOrWhiteSpace(KeySecret))
            throw new InvalidOperationException("SkipCash ClientId, KeyId, and KeySecret are required.");
    }
}