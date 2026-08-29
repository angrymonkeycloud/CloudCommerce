namespace AngryMonkey.CloudPayments.Stripe;

public sealed class StripeOptions
{
    public string SecretKey { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
    public Uri ApiBaseAddress { get; set; } = new("https://api.stripe.com/");
    public TimeSpan WebhookTolerance { get; set; } = TimeSpan.FromMinutes(5);

    // There is deliberately no ApiVersion setting. Stripe.net pins the API version it was
    // generated against (StripeConfiguration.ApiVersion) and sends it on every request, which is
    // what this integration needs: an account whose Dashboard default is some older version can
    // no longer change the shape of the responses underneath us. It also means the version is NOT
    // ours to override - the SDK's models only deserialise the version they were built for, so a
    // knob here would only ever be a way to break the client. Move versions by upgrading the
    // package.

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(SecretKey))
            throw new InvalidOperationException("Stripe SecretKey is required.");
    }
}
