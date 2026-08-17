# CloudCommerce

CloudCommerce is the optional headless composition layer for carts, checkout, orders, discounts, totals, tax orchestration, shipping selection, payment execution, bookings, and fulfillment. CloudPayments, CloudLogistics, and CloudBooking remain independently usable and never reference CloudCommerce.

## Register only what the application uses

```csharp
builder.Services.AddCloudPayments();
builder.Services.AddCloudLogistics();
builder.Services.AddCloudBooking();
builder.Services.AddCloudCommerce();

builder.Services.AddCloudCommercePayments();
builder.Services.AddCloudCommerceLogistics();
builder.Services.AddCloudCommerceBooking();
```

## Headless workflow

```csharp
Cart cart = await commerce.CreateCartAsync("USD");
cart = await commerce.AddItemAsync(cart.Id, productPresentation);
CommerceTotals totals = await commerce.CalculateTotalsAsync(cart.Id, shippingOption);

Order order = await commerce.CheckoutAsync(new CheckoutRequest
{
    CartId = cart.Id,
    IdempotencyKey = $"checkout-{cart.Id}",
    PaymentProvider = "Stripe",
    Shipping = shippingOption,
    BookingReservationIds = reservationIds
});
```

Applications own authoritative products and subscriptions. CloudCommerce accepts lightweight presentations and references; it does not introduce a universal catalog or subscription engine. Persistence is exposed through public contracts and does not depend on private CDM code.

## UI choices

Use the headless service alone, install [CloudCommerce Components](../CloudCommerce.Components/README.md), compose individual components or pages, or begin with the full storefront. Every semantic UI slot is replaceable without replacing business services. The [interactive demo](../CloudCommerce.Demo/README.md) shows the complete journey and provides code tabs for integration.

See the [commerce reference](docs/index.md), [UI guide](docs/ui.md), [CloudPayments](../CloudPayments/README.md), [CloudLogistics](../CloudLogistics/README.md), and [CloudBooking](../CloudBooking/README.md).

CloudCommerce is part of [Angry Monkey Cloud](https://angrymonkeycloud.com). Development follows the shared [AI instructions](https://github.com/angrymonkeycloud/CloudDocs/blob/main/docs/ai/instructions.md).