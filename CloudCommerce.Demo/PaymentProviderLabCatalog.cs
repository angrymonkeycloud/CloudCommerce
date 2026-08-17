namespace AngryMonkey.CloudCommerce.Demo;

public sealed record PaymentProviderLabDefinition(string Slug, string ProviderName, string DisplayName, string PackageName, string Region, string Currency, decimal SuggestedAmount, Uri DocumentationUrl, Uri TestingUrl, Uri PortalUrl, IReadOnlyList<ProviderCredentialField> Credentials, string RegistrationCode, string RequestCode, string CustomerExperience)
{
    /// <summary>False for the built-in sandbox adapter, which runs with no setup at all.</summary>
    public bool RequiresCredentials => Credentials.Count > 0;

    public IEnumerable<string> ConfigurationKeys => Credentials.Select(credential => credential.Key);
}

public static class PaymentProviderLabCatalog
{
    public static IReadOnlyList<PaymentProviderLabDefinition> All { get; } =
    [
        new("sandbox", "Demo", "Built-in sandbox", "CloudCommerce.Demo", "Local", "USD", 12m, new("https://github.com/angrymonkeycloud/CloudCommerce"), new("https://github.com/angrymonkeycloud/CloudCommerce"), new("https://github.com/angrymonkeycloud/CloudCommerce"), [],
            """
builder.Services.AddCloudPayments();
builder.Services.AddCloudPaymentProvider<DemoPaymentProvider>();
""",
            """
PaymentResult result = await payments.CreateAsync(new PaymentRequest
{
    Provider = "Demo",
    Amount = new Money("USD", 12m),
    IdempotencyKey = $"sandbox-lab-{Guid.NewGuid():N}",
    Description = "CloudCommerce sandbox lab"
});
""", "An in-memory adapter that needs no keys and no network. Use it to explore the CloudPayments contract, then switch to a real gateway once you have test credentials."),
        new("stripe", "Stripe", "Stripe", "AngryMonkey.CloudPayments.Stripe", "Global", "USD", 12m, new("https://docs.stripe.com/payments"), new("https://docs.stripe.com/testing"), new("https://dashboard.stripe.com/test/apikeys"),
            [new("Stripe:SecretKey", "Secret key", "sk_test_…"), new("Stripe:WebhookSecret", "Webhook signing secret", "whsec_…", IsRequired: false)],
            """
builder.Services.AddStripeCloudPayments(options =>
{
    options.SecretKey = configuration["Stripe:SecretKey"]!;
    options.WebhookSecret = configuration["Stripe:WebhookSecret"] ?? string.Empty;
});
""",
            """
PaymentResult result = await payments.CreateAsync(new PaymentRequest
{
    Provider = "Stripe",
    Amount = new Money("USD", 12m),
    IdempotencyKey = $"stripe-lab-{Guid.NewGuid():N}",
    Description = "CloudCommerce sandbox lab"
});
""", "Stripe returns a PaymentIntent and client-side action data. Card data belongs in Stripe.js or a Stripe-hosted surface, never CloudCommerce."),
        new("paypal", "PayPal", "PayPal", "AngryMonkey.CloudPayments.PayPal", "Global", "USD", 12m, new("https://developer.paypal.com/api/rest/"), new("https://developer.paypal.com/tools/sandbox/"), new("https://developer.paypal.com/dashboard/"),
            [new("PayPal:ClientId", "Client ID", "AY…", IsSecret: false), new("PayPal:ClientSecret", "Client secret", "EL…"), new("PayPal:WebhookId", "Webhook ID", "8SM…", IsRequired: false, IsSecret: false)],
            """
builder.Services.AddPayPalCloudPayments(options =>
{
    options.ClientId = configuration["PayPal:ClientId"]!;
    options.ClientSecret = configuration["PayPal:ClientSecret"]!;
    options.WebhookId = configuration["PayPal:WebhookId"] ?? string.Empty;
});
""",
            """
PaymentResult result = await payments.CreateAsync(new PaymentRequest
{
    Provider = "PayPal",
    Amount = new Money("USD", 12m),
    IdempotencyKey = $"paypal-lab-{Guid.NewGuid():N}",
    Description = "CloudCommerce sandbox lab"
});
""", "A successful create returns the real PayPal sandbox approval URL. Sign in using a sandbox buyer from the developer dashboard."),
        new("adyen", "Adyen", "Adyen", "AngryMonkey.CloudPayments.Adyen", "Global", "EUR", 12m, new("https://docs.adyen.com/online-payments/"), new("https://docs.adyen.com/development-resources/test-cards-and-credentials"), new("https://ca-test.adyen.com/"),
            [new("Adyen:ApiKey", "API key", "AQE…"), new("Adyen:MerchantAccount", "Merchant account", "YourCompanyECOM", IsSecret: false), new("Adyen:HmacKey", "HMAC key", "For webhook validation", IsRequired: false)],
            """
builder.Services.AddAdyenCloudPayments(options =>
{
    options.ApiKey = configuration["Adyen:ApiKey"]!;
    options.MerchantAccount = configuration["Adyen:MerchantAccount"]!;
    options.HmacKey = configuration["Adyen:HmacKey"] ?? string.Empty;
});
""",
            """
PaymentResult result = await payments.CreateAsync(new PaymentRequest
{
    Provider = "Adyen",
    Amount = new Money("EUR", 12m),
    IdempotencyKey = $"adyen-lab-{Guid.NewGuid():N}",
    Metadata = new() { ["returnUrl"] = "https://localhost/payment/return" }
});
""", "Adyen can return a redirect or provider-instruction action. Production UIs pass that action to Adyen Web Components."),
        new("myfatoorah", "MyFatoorah", "MyFatoorah", "AngryMonkey.CloudPayments.MyFatoorah", "MENA", "KWD", 3m, new("https://docs.myfatoorah.com/docs/get-started"), new("https://docs.myfatoorah.com/docs/test-cards"), new("https://portal.myfatoorah.com/"),
            [new("MyFatoorah:ApiToken", "API token", "Bearer token from the portal"), new("MyFatoorah:PaymentMethodId", "Payment method ID", "2", IsRequired: false, IsSecret: false, IsNumeric: true), new("MyFatoorah:WebhookSecret", "Webhook secret", "Optional", IsRequired: false)],
            """
builder.Services.AddMyFatoorahCloudPayments(options =>
{
    options.ApiToken = configuration["MyFatoorah:ApiToken"]!;
    options.PaymentMethodId = configuration.GetValue<int>("MyFatoorah:PaymentMethodId");
    options.WebhookSecret = configuration["MyFatoorah:WebhookSecret"] ?? string.Empty;
});
""",
            """
PaymentResult result = await payments.CreateAsync(new PaymentRequest
{
    Provider = "MyFatoorah",
    Amount = new Money("KWD", 3m),
    IdempotencyKey = $"myfatoorah-lab-{Guid.NewGuid():N}",
    Metadata = new() { ["customer_name"] = "Sandbox Customer" }
});
""", "The adapter calls MyFatoorah's test API and returns a real hosted invoice URL. Card entry stays on MyFatoorah."),
        new("skipcash", "SkipCash", "SkipCash", "AngryMonkey.CloudPayments.SkipCash", "Qatar", "QAR", 10m, new("https://skipcash.app/assets/doc/SkipCashIntegrationManual.pdf"), new("https://skipcash.app/assets/doc/SkipCashIntegrationManual.pdf"), new("https://merchantportal.skipcash.app/"),
            [new("SkipCash:ClientId", "Client ID", "Merchant portal client ID", IsSecret: false), new("SkipCash:KeyId", "Key ID", "Key identifier", IsSecret: false), new("SkipCash:KeySecret", "Key secret", "Signing secret"), new("SkipCash:WebhookKey", "Webhook key", "Optional", IsRequired: false)],
            """
builder.Services.AddSkipCashCloudPayments(options =>
{
    options.ClientId = configuration["SkipCash:ClientId"]!;
    options.KeyId = configuration["SkipCash:KeyId"]!;
    options.KeySecret = configuration["SkipCash:KeySecret"]!;
    options.WebhookKey = configuration["SkipCash:WebhookKey"] ?? string.Empty;
});
""",
            """
PaymentResult result = await payments.CreateAsync(new PaymentRequest
{
    Provider = "SkipCash",
    Amount = new Money("QAR", 10m),
    IdempotencyKey = $"skipcash-lab-{Guid.NewGuid():N}",
    Metadata = SandboxCustomer.Qatar
});
""", "SkipCash requires QAR and customer/address metadata. The lab supplies fictional customer data and signs with your configured test key."),
        new("tap", "Tap", "Tap Payments", "AngryMonkey.CloudPayments.Tap", "MENA", "KWD", 3m, new("https://developers.tap.company/docs/get-started"), new("https://developers.tap.company/reference/testing-cards"), new("https://business.tap.company/"),
            [new("Tap:SecretKey", "Secret key", "sk_test_…"), new("Tap:MerchantId", "Merchant ID", "Optional", IsRequired: false, IsSecret: false)],
            """
builder.Services.AddTapCloudPayments(options =>
{
    options.SecretKey = configuration["Tap:SecretKey"]!;
    options.MerchantId = configuration["Tap:MerchantId"] ?? string.Empty;
});
""",
            """
PaymentResult result = await payments.CreateAsync(new PaymentRequest
{
    Provider = "Tap",
    Amount = new Money("KWD", 3m),
    IdempotencyKey = $"tap-lab-{Guid.NewGuid():N}",
    Metadata = new() { ["first_name"] = "Sandbox", ["last_name"] = "Customer" }
});
""", "Tap returns a real charge and, when required, a redirect action. Use Tap's official testing-card page."),
        new("paytabs", "PayTabs", "PayTabs", "AngryMonkey.CloudPayments.PayTabs", "MENA", "AED", 25m, new("https://support.paytabs.com/en/support/solutions/articles/60000992876-3-2-1-hosted-payment-page-apis-initiating-the-payment"), new("https://support.paytabs.com/en/support/solutions/articles/60000712315-what-are-the-test-cards-available-to-perform-payments-"), new("https://merchant.paytabs.com/"),
            [new("PayTabs:ProfileId", "Profile ID", "123456", IsSecret: false, IsNumeric: true), new("PayTabs:ServerKey", "Server key", "SJ…")],
            """
builder.Services.AddPayTabsCloudPayments(options =>
{
    options.ProfileId = configuration.GetValue<long>("PayTabs:ProfileId");
    options.ServerKey = configuration["PayTabs:ServerKey"]!;
});
""",
            """
PaymentResult result = await payments.CreateAsync(new PaymentRequest
{
    Provider = "PayTabs",
    Amount = new Money("AED", 25m),
    IdempotencyKey = $"paytabs-lab-{Guid.NewGuid():N}",
    Description = "CloudCommerce sandbox lab"
});
""", "PayTabs returns a hosted payment-page URL. Complete payment there with an official test card.")
    ];

    public static int IndexOf(PaymentProviderLabDefinition definition) => All.ToList().IndexOf(definition);
    public static PaymentProviderLabDefinition? Find(string? slug) => All.FirstOrDefault(item => item.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase));
    public static string ConfigurationCommands(PaymentProviderLabDefinition definition) => string.Join(Environment.NewLine, definition.ConfigurationKeys.Select(key => $"dotnet user-secrets set \"{key}\" \"YOUR_TEST_VALUE\" --project CloudCommerce.Demo"));
}
