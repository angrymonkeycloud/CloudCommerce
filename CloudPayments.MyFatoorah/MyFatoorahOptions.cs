namespace AngryMonkey.CloudPayments.MyFatoorah;

public sealed class MyFatoorahOptions
{
    public string ApiToken { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
    public int PaymentMethodId { get; set; }
    public string Language { get; set; } = "EN";
    public Uri ApiBaseAddress { get; set; } = new("https://apitest.myfatoorah.com/");
    public Uri? ReturnUrl { get; set; }
    public Uri? ErrorUrl { get; set; }

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiToken))
            throw new InvalidOperationException("MyFatoorah ApiToken is required.");
        if (Language is not ("EN" or "AR"))
            throw new InvalidOperationException("MyFatoorah Language must be EN or AR.");
    }
}