namespace AngryMonkey.CloudPayments.PayTabs;

public sealed class PayTabsOptions
{
    public long ProfileId { get; set; }
    public string ServerKey { get; set; } = string.Empty;
    public string Language { get; set; } = "en";
    public Uri ApiBaseAddress { get; set; } = new("https://secure-global.paytabs.com/");
    public Uri? ReturnUrl { get; set; }
    public Uri? CallbackUrl { get; set; }

    internal void Validate()
    {
        if (ProfileId <= 0 || string.IsNullOrWhiteSpace(ServerKey))
            throw new InvalidOperationException("PayTabs ProfileId and ServerKey are required.");
        if (Language is not ("en" or "ar"))
            throw new InvalidOperationException("PayTabs Language must be en or ar.");
    }
}