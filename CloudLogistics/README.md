# CloudLogistics

CloudLogistics is a standalone inventory, fulfillment, shipping, tracking, delivery, and carrier abstraction. It does not depend on CloudCommerce. Carrier-specific HTTP contracts stay in adapter packages, while applications consume stable rates, shipments, labels, and tracking events.

## Install and register

```powershell
dotnet add package AngryMonkey.CloudLogistics
dotnet add package AngryMonkey.CloudLogistics.Aramex
```

```csharp
builder.Services.AddCloudLogistics();
builder.Services.AddAramexCloudLogistics(options =>
{
    options.UserName = configuration["Aramex:UserName"]!;
    options.Password = configuration["Aramex:Password"]!;
    options.AccountNumber = configuration["Aramex:AccountNumber"]!;
});
```

## Supported carriers

| Adapter | Capabilities | Official developer resources |
| --- | --- | --- |
| `Aramex` | Rates, shipment creation, URL labels, tracking, development/production endpoints | [Shipping Services API manual](https://www.aramex.com/docs/default-source/resourses/resourcesdata/shipping-services-api-manual.pdf) |
| `DhlExpress` | MyDHL rating, products, shipment and label generation, tracking, cancellation, documents | [MyDHL API](https://developer.dhl.com/api-reference/dhl-express-mydhl-api) |
| `FedEx` | OAuth, account rates, shipment and label creation, tracking, cancellation, sandbox/production | [Get started](https://developer.fedex.com/api/en-us/get-started.html) · [test credentials](https://developer.fedex.com/api/en-us/get-started/shipper.html) |
| `Wakilni` | Lebanon and Jordan last-mile delivery: bulk bookings, cash on delivery, barcode labels, tracking and cancellation by Wakilni order id, delivery areas | [API reference](https://wiki.wakilni.com/integrations/api) · [developer sign-up](https://dev.wakilni.com/sign-up) |

Every provider can also implement `TrackAsync` and `CancelAsync` by the carrier reference kept on `Shipment.ProviderReference`, so an app can follow shipments it stored itself after a restart. `ShipmentRequest` carries optional `CashOnDelivery`, `Items`, `Notes` and `Metadata` for carriers that need them, and `LogisticsAddress` an optional email and coordinates.

## Domain responsibilities

- Inventory: warehouses, locations, stock levels, availability, reservations, adjustments, transfers, and movements.
- Fulfillment: reserve, pick, pack, prepare shipment, partial fulfillment, and split fulfillment.
- Shipping: methods, rates, shipments, labels, tracking, delivery states, pickup locations, and carrier adapters.
- Geography: `LogisticsAddress` carries delivery details while country and subdivision codes integrate with CloudGeography instead of duplicating geography models.

## Test safely

Adapter tests inject deterministic HTTP handlers and captured response shapes. They do not call carrier sandboxes. The [interactive demo](../CloudCommerce.Demo/README.md) provides working local rate selection and tracking progression, with links to official onboarding material. See the detailed [carrier reference](docs/carriers.md) and [CloudCommerce integration](../CloudCommerce/README.md).

CloudLogistics is part of [Angry Monkey Cloud](https://angrymonkeycloud.com). Development follows the shared [AI instructions](https://github.com/angrymonkeycloud/CloudDocs/blob/main/docs/ai/instructions.md).