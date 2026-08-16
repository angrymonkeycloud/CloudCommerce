namespace AngryMonkey.CloudLogistics.DhlExpress;

public sealed class DhlExpressOptions
{
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public Uri ApiBaseAddress { get; set; } = new("https://express.api.dhl.com/mydhlapi/test/");

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(UserName) || string.IsNullOrWhiteSpace(Password) || string.IsNullOrWhiteSpace(AccountNumber))
            throw new InvalidOperationException("DHL Express UserName, Password, and AccountNumber are required.");
    }
}
