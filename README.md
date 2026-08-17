# Angry Monkey Cloud Commerce

[![Website](https://img.shields.io/badge/Angry_Monkey_Cloud-website-6d4aff)](https://angrymonkeycloud.com)
[![GitHub](https://img.shields.io/badge/GitHub-CloudCommerce-181717?logo=github)](https://github.com/angrymonkeycloud/CloudCommerce)
[![Commerce ecosystem](https://github.com/angrymonkeycloud/CloudCommerce/actions/workflows/commerce.yml/badge.svg)](https://github.com/angrymonkeycloud/CloudCommerce/actions/workflows/commerce.yml)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512bd4)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

CloudCommerce is a modular .NET commerce ecosystem. Use CloudPayments, CloudLogistics, or CloudBooking independently, or compose them through CloudCommerce and its replaceable Blazor components. Applications keep ownership of product and subscription meaning; provider packages remain isolated; public projects do not depend on private CDM persistence.

## Packages

| Division | Package | NuGet |
| --- | --- | --- |
| Payments | `AngryMonkey.CloudPayments` | [![NuGet](https://img.shields.io/nuget/v/AngryMonkey.CloudPayments)](https://www.nuget.org/packages/AngryMonkey.CloudPayments) |
| Payments | `AngryMonkey.CloudPayments.Stripe` | [![NuGet](https://img.shields.io/nuget/v/AngryMonkey.CloudPayments.Stripe)](https://www.nuget.org/packages/AngryMonkey.CloudPayments.Stripe) |
| Payments | `AngryMonkey.CloudPayments.PayPal` | [![NuGet](https://img.shields.io/nuget/v/AngryMonkey.CloudPayments.PayPal)](https://www.nuget.org/packages/AngryMonkey.CloudPayments.PayPal) |
| Payments | `AngryMonkey.CloudPayments.Adyen` | [![NuGet](https://img.shields.io/nuget/v/AngryMonkey.CloudPayments.Adyen)](https://www.nuget.org/packages/AngryMonkey.CloudPayments.Adyen) |
| Payments | `AngryMonkey.CloudPayments.MyFatoorah` | [![NuGet](https://img.shields.io/nuget/v/AngryMonkey.CloudPayments.MyFatoorah)](https://www.nuget.org/packages/AngryMonkey.CloudPayments.MyFatoorah) |
| Payments | `AngryMonkey.CloudPayments.SkipCash` | [![NuGet](https://img.shields.io/nuget/v/AngryMonkey.CloudPayments.SkipCash)](https://www.nuget.org/packages/AngryMonkey.CloudPayments.SkipCash) |
| Payments | `AngryMonkey.CloudPayments.Tap` | [![NuGet](https://img.shields.io/nuget/v/AngryMonkey.CloudPayments.Tap)](https://www.nuget.org/packages/AngryMonkey.CloudPayments.Tap) |
| Payments | `AngryMonkey.CloudPayments.PayTabs` | [![NuGet](https://img.shields.io/nuget/v/AngryMonkey.CloudPayments.PayTabs)](https://www.nuget.org/packages/AngryMonkey.CloudPayments.PayTabs) |
| Logistics | `AngryMonkey.CloudLogistics` | [![NuGet](https://img.shields.io/nuget/v/AngryMonkey.CloudLogistics)](https://www.nuget.org/packages/AngryMonkey.CloudLogistics) |
| Logistics | `AngryMonkey.CloudLogistics.Aramex` | [![NuGet](https://img.shields.io/nuget/v/AngryMonkey.CloudLogistics.Aramex)](https://www.nuget.org/packages/AngryMonkey.CloudLogistics.Aramex) |
| Logistics | `AngryMonkey.CloudLogistics.DhlExpress` | [![NuGet](https://img.shields.io/nuget/v/AngryMonkey.CloudLogistics.DhlExpress)](https://www.nuget.org/packages/AngryMonkey.CloudLogistics.DhlExpress) |
| Logistics | `AngryMonkey.CloudLogistics.FedEx` | [![NuGet](https://img.shields.io/nuget/v/AngryMonkey.CloudLogistics.FedEx)](https://www.nuget.org/packages/AngryMonkey.CloudLogistics.FedEx) |
| Booking | `AngryMonkey.CloudBooking` | [![NuGet](https://img.shields.io/nuget/v/AngryMonkey.CloudBooking)](https://www.nuget.org/packages/AngryMonkey.CloudBooking) |
| Commerce | `AngryMonkey.CloudCommerce` | [![NuGet](https://img.shields.io/nuget/v/AngryMonkey.CloudCommerce)](https://www.nuget.org/packages/AngryMonkey.CloudCommerce) |
| UI | `AngryMonkey.CloudCommerce.Components` | [![NuGet](https://img.shields.io/nuget/v/AngryMonkey.CloudCommerce.Components)](https://www.nuget.org/packages/AngryMonkey.CloudCommerce.Components) |

## Choose a division

- [CloudPayments](CloudPayments/README.md): normalized transactions, tax extension points, webhooks, seven gateway adapters, and official sandbox resources.
- [CloudLogistics](CloudLogistics/README.md): inventory, fulfillment, shipping, tracking, and Aramex, DHL Express, and FedEx adapters.
- [CloudBooking](CloudBooking/README.md): standalone availability, capacity, reservations, rescheduling, and persistence contracts.
- [CloudCommerce](CloudCommerce/README.md): headless cart, totals, checkout, order, and cross-domain orchestration.
- [CloudCommerce Components](CloudCommerce.Components/README.md): replaceable Blazor components and composition slots.
- [Interactive demo](CloudCommerce.Demo/README.md): runnable previews, working workflows, and copyable code tabs.

## Run the developer demo

```powershell
dotnet run --project CloudCommerce.Demo/CloudCommerce.Demo.csproj
```

The storefront works without credentials using a clearly labeled local driver. The payment area provides seven dedicated real sandbox labs: configure provider-issued test credentials with user-secrets, execute the actual adapter, inspect the normalized result, and open the returned provider-hosted checkout. The component catalog provides a separate live route for every shipped Razor component.


## Build and test

```powershell
dotnet build CloudCommerce.slnx -c Release
dotnet test CloudCommerce.slnx -c Release
```

CloudCommerce is part of [Angry Monkey Cloud](https://angrymonkeycloud.com). Development follows the shared [AI instructions](https://github.com/angrymonkeycloud/CloudDocs/blob/main/docs/ai/instructions.md).