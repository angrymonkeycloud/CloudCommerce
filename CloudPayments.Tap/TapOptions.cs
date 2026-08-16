namespace AngryMonkey.CloudPayments.Tap;

public sealed class TapOptions
{
    public string SecretKey { get; set; } = string.Empty;
    public string MerchantId { get; set; } = string.Empty;
    public Uri ApiBaseAddress { get; set; } = new("https://api.tap.company/");
    public Uri? RedirectUrl { get; set; }
    public Uri? PostUrl { get; set; }

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(SecretKey))
            throw new InvalidOperationException("Tap SecretKey is required.");
    }
}