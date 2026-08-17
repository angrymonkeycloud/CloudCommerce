namespace AngryMonkey.CloudCommerce.Demo;

public static class DemoCodeSamples
{
    public const string Composition = """
builder.Services.AddCloudPayments();
builder.Services.AddCloudLogistics();
builder.Services.AddCloudBooking();
builder.Services.AddCloudCommerce();

builder.Services.AddCloudCommercePayments();
builder.Services.AddCloudCommerceLogistics();
builder.Services.AddCloudCommerceBooking();
builder.Services.AddCloudCommerceComponents();
""";

    public const string ProductCard = """
<ProductCard Product="product" OnAdd="AddToCartAsync" />

@code {
    private async Task AddToCartAsync(ProductPresentation product)
    {
        cart = await commerce.AddItemAsync(cart.Id, product);
    }
}
""";

    public const string PaymentSelector = """
<PaymentSelector Options="paymentOptions"
                 SelectedProvider="selectedProvider"
                 OnSelected="SelectProvider" />

@code {
    private string selectedProvider = "MyFatoorah";
    private void SelectProvider(string provider) => selectedProvider = provider;
}
""";

    public const string BookingSelector = """
<BookingSelector Slots="availableSlots"
                 Selected="selectedSlot"
                 OnSelected="SelectSlot"
                 TimeZoneLabel="UTC" />

@code {
    private TimeSlot? selectedSlot;
    private void SelectSlot(TimeSlot slot) => selectedSlot = slot;
}
""";

    public const string ShipmentTracker = """
<ShipmentTracker Shipment="shipment" Events="trackingEvents" />
""";

    public const string Storefront = """
Cart cart = await commerce.CreateCartAsync("USD");
cart = await commerce.AddItemAsync(cart.Id, product);
CommerceTotals totals = await commerce.CalculateTotalsAsync(cart.Id, shipping);

Order order = await commerce.CheckoutAsync(new CheckoutRequest
{
    CartId = cart.Id,
    IdempotencyKey = $"checkout-{cart.Id}",
    PaymentProvider = "Stripe",
    Shipping = shipping,
    BookingReservationIds = reservationIds
});
""";

    public const string Booking = """
services.AddCloudBooking();

IReadOnlyList<TimeSlot> slots = await availability.GetAvailableSlotsAsync(
    new AvailabilityRequest(resourceId, service, rangeStart, rangeEnd));

Reservation reservation = await booking.ReserveAsync(
    new ReservationRequest(resourceId, service, selectedSlot.StartsAt));
""";

    public const string Logistics = """
services.AddCloudLogistics();
services.AddAramexCloudLogistics(options =>
{
    options.UserName = configuration["Aramex:UserName"]!;
    options.Password = configuration["Aramex:Password"]!;
    options.AccountNumber = configuration["Aramex:AccountNumber"]!;
});

IShippingProvider aramex = providers.Get("Aramex");
IReadOnlyList<ShippingRate> rates = await aramex.GetRatesAsync(request);
""";

    public const string Overrides = """
services.AddCloudCommerceComponents(options =>
{
    options.Replace<MyProductCard>(CommerceComponentSlots.ProductCard);
    options.Replace<MyPaymentSelector>(CommerceComponentSlots.PaymentSelector);
    options.Replace<MyBookingCalendar>(CommerceComponentSlots.BookingSelector);
});
""";

    public const string Architecture = """
<ProjectReference Include="..\CloudPayments\CloudPayments.csproj" />
<ProjectReference Include="..\CloudLogistics\CloudLogistics.csproj" />
<ProjectReference Include="..\CloudBooking\CloudBooking.csproj" />
<ProjectReference Include="..\CloudCommerce\CloudCommerce.csproj" />

<!-- Specialized domains never reference CloudCommerce. -->
""";

    public static string PaymentRegistration(string provider) => provider switch
    {
        "Stripe" => """
services.AddStripeCloudPayments(options =>
{
    options.SecretKey = configuration["Stripe:SecretKey"]!;
    options.WebhookSecret = configuration["Stripe:WebhookSecret"]!;
});
""",
        "PayPal" => """
services.AddPayPalCloudPayments(options =>
{
    options.ClientId = configuration["PayPal:ClientId"]!;
    options.ClientSecret = configuration["PayPal:ClientSecret"]!;
});
""",
        "Adyen" => """
services.AddAdyenCloudPayments(options =>
{
    options.ApiKey = configuration["Adyen:ApiKey"]!;
    options.MerchantAccount = configuration["Adyen:MerchantAccount"]!;
});
""",
        "MyFatoorah" => """
services.AddMyFatoorahCloudPayments(options =>
{
    options.ApiToken = configuration["MyFatoorah:ApiToken"]!;
    options.WebhookSecret = configuration["MyFatoorah:WebhookSecret"]!;
    options.PaymentMethodId = 2;
});
""",
        "SkipCash" => """
services.AddSkipCashCloudPayments(options =>
{
    options.ClientId = configuration["SkipCash:ClientId"]!;
    options.KeyId = configuration["SkipCash:KeyId"]!;
    options.KeySecret = configuration["SkipCash:KeySecret"]!;
});
""",
        "Tap Payments" => """
services.AddTapCloudPayments(options =>
{
    options.SecretKey = configuration["Tap:SecretKey"]!;
    options.MerchantId = configuration["Tap:MerchantId"]!;
});
""",
        "PayTabs" => """
services.AddPayTabsCloudPayments(options =>
{
    options.ProfileId = configuration.GetValue<long>("PayTabs:ProfileId");
    options.ServerKey = configuration["PayTabs:ServerKey"]!;
});
""",
        _ => string.Empty
    };

    public static string PaymentRequest(string provider) => $$"""
IPaymentService payments = serviceProvider.GetRequiredService<IPaymentService>();

PaymentResult result = await payments.CreateAsync(new PaymentRequest
{
    Provider = "{{provider}}",
    Amount = new Money("USD", 114m),
    IdempotencyKey = $"order-{orderId}",
    Description = "Order payment"
});

if (result.Payment?.NextAction is { } action)
    navigation.NavigateTo(action.Url.ToString(), forceLoad: true);
""";
}