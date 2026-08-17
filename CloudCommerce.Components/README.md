# CloudCommerce Components

CloudCommerce Components provides reusable, replaceable Blazor UI over the headless commerce contracts. Business logic remains in services; components receive models and emit typed events.

## Register

```csharp
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCloudCommerceComponents();
```

Add the package stylesheet and choose the render mode appropriate for the host application.

## Reusable components

| Component | Use it for |
| --- | --- |
| `ProductCard` | Compact application-owned product presentation and add action |
| `ProductInformation` | Full product detail presentation |
| `CartView`, `CartItemView`, `CouponInput` | Cart editing and discounts |
| `ShippingSelector` | Normalized CloudLogistics options |
| `PaymentSelector` | Provider choice without transaction logic in markup |
| `BookingSelector` | CloudBooking time-slot selection |
| `ShipmentTracker` | Normalized shipment state and event timeline |
| `CheckoutProgress`, `OrderSummary` | Checkout and confirmation composition |

```razor
<ProductCard Product="product" OnAdd="AddToCartAsync" />
<PaymentSelector Options="paymentOptions"
                 SelectedProvider="selectedProvider"
                 OnSelected="SelectProvider" />
```

## Replace a default

```csharp
builder.Services.AddCloudCommerceComponents(options =>
{
    options.Replace<MyProductCard>(CommerceComponentSlots.ProductCard);
    options.Replace<MyPaymentSelector>(CommerceComponentSlots.PaymentSelector);
});
```

Source styles are authored in `src/css/cloud-commerce.less`; generated CSS is not hand-edited. Browse every live component and copy its integration code from the [developer demo](../CloudCommerce.Demo/README.md). See also the [UI guide](../CloudCommerce/docs/ui.md).


## Navigation versus selection

`ProviderCard` distinguishes navigation from selection. Use `ActionHref` when the primary action opens a provider page; the component renders a real link. Use `OnSelected` only for an explicit choice inside an existing workflow. The component never labels a selection callback as “try the flow.”

```razor
<ProviderCard Model="provider"
              ActionHref="/payments/stripe"
              ActionLabel="Open sandbox lab" />
```

The [component catalog](../CloudCommerce.Demo/README.md#public-component-reference) has a dedicated live page for every shipped component, including package, namespace, source, parameters, events, and copyable code.

CloudCommerce Components is part of [Angry Monkey Cloud](https://angrymonkeycloud.com). Development follows the shared [AI instructions](https://github.com/angrymonkeycloud/CloudDocs/blob/main/docs/ai/instructions.md).