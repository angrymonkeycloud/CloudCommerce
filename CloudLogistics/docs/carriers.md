# CloudLogistics carrier adapters and sandbox resources

CloudLogistics carrier packages translate common rate, shipment, label, tracking, pickup, and cancellation concepts into independent carrier APIs. Carrier payloads remain inside their adapter; CloudLogistics exposes stable contracts to applications and CloudCommerce.

## Supported carrier adapters

| Package | Implemented foundation | Official developer and test resources |
| --- | --- | --- |
| `AngryMonkey.CloudLogistics.Aramex` | Rates, shipment creation, URL labels, and tracking through development or production endpoints | [Aramex Shipping Services API manual](https://www.aramex.com/docs/default-source/resourses/resourcesdata/shipping-services-api-manual.pdf) |
| `AngryMonkey.CloudLogistics.DhlExpress` | MyDHL rating, products, shipment/label generation, tracking, shipment cancellation, and preserved document payloads | [DHL Express MyDHL API](https://developer.dhl.com/api-reference/dhl-express-mydhl-api) |
| `AngryMonkey.CloudLogistics.FedEx` | OAuth, account rates, shipment/label creation, tracking, cancellation, sandbox or production endpoints | [FedEx developer getting started](https://developer.fedex.com/api/en-us/get-started.html) · [test credential workflow](https://developer.fedex.com/api/en-us/get-started/shipper.html) |

DHL documents a dedicated MyDHL test environment, while FedEx supplies test keys through a developer project and virtualizes parts of its sandbox. Aramex development endpoints and credentials are described in its integration manual. Always follow current carrier onboarding and any label-certification requirements before production.

## Geography boundary

Carrier requests map `LogisticsAddress` country and subdivision codes through CloudGeography. The logistics contract adds delivery-specific address lines, recipient details, and postal codes; it does not create competing country or administrative-division models.

## Credential and test safety

Supply credentials through user secrets, environment variables, or managed secret storage. Unit tests use injected HTTP handlers and captured response shapes; they never call carrier sandboxes. The CloudCommerce demo simulates rate selection and tracking progression locally while linking to the official resources above.

See [CloudLogistics](index.md), [payment providers](../../CloudPayments/docs/providers.md), the [CloudCommerce demo](../../CloudCommerce/docs/demo.md), and the [commerce ecosystem architecture](../../docs/commerce-ecosystem/index.md).