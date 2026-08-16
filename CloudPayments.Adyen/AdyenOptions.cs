namespace AngryMonkey.CloudPayments.Adyen;

public sealed class AdyenOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string MerchantAccount { get; set; } = string.Empty;
    public string HmacKey { get; set; } = string.Empty;
    public Uri ApiBaseAddress { get; set; } = new("https://checkout-test.adyen.com/v71/");

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey) || string.IsNullOrWhiteSpace(MerchantAccount))
            throw new InvalidOperationException("Adyen ApiKey and MerchantAccount are required.");
    }
}
