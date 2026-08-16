namespace AngryMonkey.CloudLogistics.FedEx;

public sealed class FedExOptions
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public Uri ApiBaseAddress { get; set; } = new("https://apis-sandbox.fedex.com/");

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ClientSecret) || string.IsNullOrWhiteSpace(AccountNumber))
            throw new InvalidOperationException("FedEx ClientId, ClientSecret, and AccountNumber are required.");
    }
}
