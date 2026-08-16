namespace AngryMonkey.CloudLogistics.Aramex;

public sealed class AramexOptions
{
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string AccountPin { get; set; } = string.Empty;
    public string AccountEntity { get; set; } = string.Empty;
    public string AccountCountryCode { get; set; } = string.Empty;
    public int LabelReportId { get; set; } = 9729;
    public Uri ApiBaseAddress { get; set; } = new("https://ws.dev.aramex.net/");

    internal void Validate()
    {
        if (new[] { UserName, Password, AccountNumber, AccountPin, AccountEntity, AccountCountryCode }.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("All Aramex account credentials and country settings are required.");
    }
}
