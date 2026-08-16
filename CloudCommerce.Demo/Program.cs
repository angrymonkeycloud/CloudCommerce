using AngryMonkey.CloudBooking;
using AngryMonkey.CloudCommerce;
using AngryMonkey.CloudCommerce.Components;
using AngryMonkey.CloudCommerce.Demo;
using AngryMonkey.CloudCommerce.Demo.Components;
using AngryMonkey.CloudLogistics;
using AngryMonkey.CloudLogistics.Aramex;
using AngryMonkey.CloudLogistics.DhlExpress;
using AngryMonkey.CloudLogistics.FedEx;
using AngryMonkey.CloudPayments;
using AngryMonkey.CloudPayments.Adyen;
using AngryMonkey.CloudPayments.MyFatoorah;
using AngryMonkey.CloudPayments.PayPal;
using AngryMonkey.CloudPayments.PayTabs;
using AngryMonkey.CloudPayments.SkipCash;
using AngryMonkey.CloudPayments.Stripe;
using AngryMonkey.CloudPayments.Tap;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCloudPayments();
builder.Services.AddCloudPaymentProvider<DemoPaymentProvider>();
builder.Services.AddCloudLogistics();
builder.Services.AddCloudShippingProvider<DemoShippingProvider>();
builder.Services.AddCloudBooking();
builder.Services.AddCloudCommerce();
builder.Services.AddSingleton<IDiscountProvider, DemoDiscountProvider>();
builder.Services.AddSingleton<ICommerceTaxProvider, DemoCommerceTaxProvider>();
builder.Services.AddCloudCommercePayments();
builder.Services.AddCloudCommerceLogistics();
builder.Services.AddCloudCommerceBooking();
builder.Services.AddCloudCommerceComponents();
builder.Services.AddScoped<PaymentFlowSimulator>();

IConfiguration configuration = builder.Configuration;

if (!string.IsNullOrWhiteSpace(configuration["Stripe:SecretKey"]))
    builder.Services.AddStripeCloudPayments(options =>
    {
        options.SecretKey = configuration["Stripe:SecretKey"]!;
        options.WebhookSecret = configuration["Stripe:WebhookSecret"] ?? string.Empty;
    });

if (!string.IsNullOrWhiteSpace(configuration["PayPal:ClientId"]))
    builder.Services.AddPayPalCloudPayments(options =>
    {
        options.ClientId = configuration["PayPal:ClientId"]!;
        options.ClientSecret = configuration["PayPal:ClientSecret"]!;
        options.WebhookId = configuration["PayPal:WebhookId"] ?? string.Empty;
    });

if (!string.IsNullOrWhiteSpace(configuration["Adyen:ApiKey"]))
    builder.Services.AddAdyenCloudPayments(options =>
    {
        options.ApiKey = configuration["Adyen:ApiKey"]!;
        options.MerchantAccount = configuration["Adyen:MerchantAccount"]!;
        options.HmacKey = configuration["Adyen:HmacKey"] ?? string.Empty;
    });

if (!string.IsNullOrWhiteSpace(configuration["MyFatoorah:ApiToken"]))
    builder.Services.AddMyFatoorahCloudPayments(options =>
    {
        options.ApiToken = configuration["MyFatoorah:ApiToken"]!;
        options.WebhookSecret = configuration["MyFatoorah:WebhookSecret"] ?? string.Empty;
        options.PaymentMethodId = configuration.GetValue<int>("MyFatoorah:PaymentMethodId");
    });

if (!string.IsNullOrWhiteSpace(configuration["SkipCash:ClientId"]))
    builder.Services.AddSkipCashCloudPayments(options =>
    {
        options.ClientId = configuration["SkipCash:ClientId"]!;
        options.KeyId = configuration["SkipCash:KeyId"]!;
        options.KeySecret = configuration["SkipCash:KeySecret"]!;
        options.WebhookKey = configuration["SkipCash:WebhookKey"] ?? string.Empty;
    });

if (!string.IsNullOrWhiteSpace(configuration["Tap:SecretKey"]))
    builder.Services.AddTapCloudPayments(options =>
    {
        options.SecretKey = configuration["Tap:SecretKey"]!;
        options.MerchantId = configuration["Tap:MerchantId"] ?? string.Empty;
    });

if (!string.IsNullOrWhiteSpace(configuration["PayTabs:ServerKey"]))
    builder.Services.AddPayTabsCloudPayments(options =>
    {
        options.ProfileId = configuration.GetValue<long>("PayTabs:ProfileId");
        options.ServerKey = configuration["PayTabs:ServerKey"]!;
    });

if (!string.IsNullOrWhiteSpace(configuration["Aramex:UserName"]))
    builder.Services.AddAramexCloudLogistics(options =>
    {
        options.UserName = configuration["Aramex:UserName"]!;
        options.Password = configuration["Aramex:Password"]!;
        options.AccountNumber = configuration["Aramex:AccountNumber"]!;
        options.AccountPin = configuration["Aramex:AccountPin"]!;
        options.AccountEntity = configuration["Aramex:AccountEntity"]!;
        options.AccountCountryCode = configuration["Aramex:AccountCountryCode"]!;
    });

if (!string.IsNullOrWhiteSpace(configuration["DhlExpress:UserName"]))
    builder.Services.AddDhlExpressCloudLogistics(options =>
    {
        options.UserName = configuration["DhlExpress:UserName"]!;
        options.Password = configuration["DhlExpress:Password"]!;
        options.AccountNumber = configuration["DhlExpress:AccountNumber"]!;
    });

if (!string.IsNullOrWhiteSpace(configuration["FedEx:ClientId"]))
    builder.Services.AddFedExCloudLogistics(options =>
    {
        options.ClientId = configuration["FedEx:ClientId"]!;
        options.ClientSecret = configuration["FedEx:ClientSecret"]!;
        options.AccountNumber = configuration["FedEx:AccountNumber"]!;
    });

WebApplication app = builder.Build();
app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();