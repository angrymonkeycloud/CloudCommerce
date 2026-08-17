# CloudLogistics inventory, fulfillment, shipping, and delivery

CloudLogistics provides independent inventory, fulfillment, carrier, shipment, label, tracking, delivery, and pickup-location contracts.

## Inventory and fulfillment responsibilities

`IInventoryService` supports stock availability, reservations, releases, adjustments, and transfers. `IInventoryStore` is database agnostic. The included implementation is deterministic and in-memory, allowing application and orchestration tests to run without Cosmos or Azure Storage.

Fulfillment contracts model reservation, pick, pack, shipment preparation, partial fulfillment, and split shipment identifiers without referencing carts or orders.

## Shipping provider model

Carrier adapters implement `IShippingProvider`, advertise `ShippingProviderCapabilities`, and remain independently testable. The core handles rate, shipment, label, tracking, cancellation, and pickup-location contracts without embedding a carrier SDK. The first adapters cover [Aramex, DHL Express, and FedEx](carriers.md).

## CloudGeography integration

`LogisticsAddress` stores CloudGeography country, subdivision, and child-subdivision codes. `CloudGeographyAddressValidator` resolves those codes through the official `CloudGeographyClient`. It does not introduce competing country or region entities.

## What CloudLogistics does not own

CloudLogistics does not own carts, product pricing, checkout, payments, or commerce orders. Item references point back to application-owned records.

See the [ecosystem architecture](../../README.md) and [CloudCommerce orchestration](../../CloudCommerce/docs/index.md).
