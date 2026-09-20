using AngryMonkey.CloudBooking;
using AngryMonkey.CloudCommerce;
using AngryMonkey.CloudCommerce.Components;
using AngryMonkey.CloudCommerce.Demo;
using AngryMonkey.CloudCommerce.Demo.Components;
using AngryMonkey.CloudLogistics;
using AngryMonkey.CloudPayments;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCloudPayments();
builder.Services.AddCloudLogistics();
builder.Services.AddCloudBooking();
builder.Services.AddCloudCommerce();
builder.Services.AddSingleton<IDiscountProvider, DemoDiscountProvider>();
builder.Services.AddSingleton<ICommerceTaxProvider, DemoCommerceTaxProvider>();
builder.Services.AddCloudCommercePayments();
builder.Services.AddCloudCommerceLogistics();
builder.Services.AddCloudCommerceBooking();
builder.Services.AddCloudCommerceComponents();
builder.Services.AddDemoSessionStores();
builder.Services.AddHttpClient(nameof(RuntimePaymentProviderFactory))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
    .RemoveAllLoggers();
builder.Services.AddScoped(serviceProvider =>
{
    ProviderCredentialStore credentials = new();
    if (builder.Environment.IsDevelopment())
        foreach (PaymentProviderLabDefinition definition in PaymentProviderLabCatalog.All)
            credentials.SeedFrom(builder.Configuration, definition);
    return credentials;
});
builder.Services.AddScoped<RuntimePaymentProviderFactory>();
builder.Services.AddScoped<SandboxPaymentRunner>();

WebApplication app = builder.Build();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}
app.UseHttpsRedirection();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    if (!context.Request.Path.StartsWithSegments("/_framework") && !context.Request.Path.StartsWithSegments("/css"))
        context.Response.Headers.CacheControl = "no-store";
    await next();
});
app.UseAntiforgery();
app.MapGet("/error", () => Results.Problem("The demo could not complete this request. Reload the workshop to start a new session."));
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
