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
- **Delivery options**: request metadata also carries `WakilniMetadata.PackageType` (a `WakilniPackageTypes` id, for every item), `CarNeeded`, `RequireSignature`, `RequirePicture`, `TryOn` and `Exchange` (`"true"` or `"false"`). Wakilni only takes signature, picture, try on and exchange when updating an order, so the provider sends them right after booking; if Wakilni refuses them the delivery stays booked and `WakilniBooking.Warning` says so.
- **Changes**: `UpdateAsync(orderId, request)` sends the recipient, address, collection, packages and options again. Wakilni accepts changes while the order is still pending and answers 422 afterwards (`WakilniException`).
- **Tracking**: keep `Shipment.ProviderReference` (the Wakilni order id). `GetHistoryAsync` returns every step oldest first with the driver's comments; `TrackAsync` maps the latest step to `ShipmentStatuses`, with the raw code, label and comments in the shipment metadata (`WakilniStatuses` lists the codes). `WakilniStatuses.IsFinal` tells when Wakilni stops moving an order. A delivery the recipient refused ends with the success code and a status such as "Success, Rejected": `WakilniStatuses.IsRejected` detects it and it maps to `Returned`, not `Delivered`. Wakilni lists the newest step first and times steps to the second, so steps in the same second keep that order.
- **Cancellation**: `CancelAsync` works while the Wakilni order is still pending.
- **No shared sandbox**: Wakilni publishes no test credentials, and live keys do not work on the testing address. Without testing keys, an integration can be checked against the live API with a delivery clearly marked as a test (waybill, recipient name and note) that is cancelled straight after booking.
- **Rates**: Wakilni prices deliveries in its own back office, so `GetRatesAsync` is not supported.
