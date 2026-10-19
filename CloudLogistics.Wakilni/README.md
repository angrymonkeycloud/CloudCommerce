# CloudLogistics Wakilni Provider

Deliveries, cash on delivery, labels, tracking and cancellation with [Wakilni](https://wiki.wakilni.com/integrations/api), the last-mile courier in Lebanon and Jordan, for `AngryMonkey.CloudLogistics`.

```csharp
services.AddWakilniCloudLogistics(options =>
{
    options.Key = configuration["Wakilni:Key"]!;
    options.Secret = configuration["Wakilni:Secret"]!;
    options.ApiBaseAddress = WakilniOptions.TestingAddress; // ProductionAddress (default) or JordanAddress
    options.PickupLocationId = 1;
});
```

Create the key and secret in the Wakilni back office under **Settings, Api Tokens, Link to your custom app**. Testing needs a client registered at https://dev.wakilni.com/sign-up.

- **Booking**: Wakilni groups deliveries in a pickup bulk. `CreateShipmentAsync` books one delivery in its own bulk; `CreateShipmentsAsync` books several in one bulk so the driver collects them in a single visit, and reports a rejected delivery on its `WakilniBooking` without failing the others.
- **Request mapping**: the destination's recipient name, phone, email, coordinates and locality (sent as the Wakilni area; prefer a name from `GetAreasAsync`) fill the receiver. `Line1` and `Line2` become the driver directions, `Notes` the order note, `Items` the packages (type from `PackageTypeId`), `Reference` the waybill. Set `CashOnDelivery` for the driver to collect the amount; leave it null for a paid order. The `express` service code books an express delivery for accounts Wakilni allows it for.
- **Ids**: Wakilni asks for numeric receiver and location ids from the sender's system. The provider derives stable ones from the phone and address unless `WakilniMetadata.ReceiverId` and `WakilniMetadata.ReceiverLocationId` are set in the request metadata.
- **Tracking**: keep `Shipment.ProviderReference` (the Wakilni order id) and call `TrackAsync` with it; the latest status is mapped to `ShipmentStatuses`, and the raw code, label and comments are in the shipment metadata (`WakilniStatuses` lists the codes). `WakilniStatuses.IsFinal` tells when Wakilni stops moving an order.
- **Cancellation**: `CancelAsync` works while the Wakilni order is still pending.
- **Rates**: Wakilni prices deliveries in its own back office, so `GetRatesAsync` is not supported.
