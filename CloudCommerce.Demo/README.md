# CloudCommerce developer workshop

[![Angry Monkey Cloud](https://img.shields.io/badge/Angry_Monkey_Cloud-Website-6d4aff?style=flat-square)](https://angrymonkeycloud.com) [![Build](https://github.com/angrymonkeycloud/CloudCommerce/actions/workflows/commerce.yml/badge.svg)](https://github.com/angrymonkeycloud/CloudCommerce/actions/workflows/commerce.yml) [![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?style=flat-square)](https://dotnet.microsoft.com/)

CloudCommerce.Demo is the workshop for the commerce ecosystem. Learning instructions, runnable scenarios and copyable code live together in the application.

## Start the workshop

Install the .NET 10 SDK, then run from the repository root:

~~~powershell
dotnet run --project CloudCommerce.Demo
~~~

Open the local HTTPS address printed by the application. Choose any demo in the searchable left navigation:

- **View** runs the scenario.
- **Code** offers integration excerpts and complete source from the same build, with copy and download actions.
- **Instructions** covers setup, scenario actions, expected results, public parameters, callbacks and troubleshooting.
- **Configuration** opens the right panel for feature switches, fixtures, booking settings and provider test credentials.

Tabs preserve the current demo state and support direct links such as /booking?tab=instructions. Arrow keys, Home and End move between tabs. Smaller screens use a navigation drawer and stack configuration below the demo.

## Workshop coverage

| Route | Experience |
| --- | --- |
| /storefront | Catalog, cart quantities, coupons, delivery and booking switches, safe checkout and order reference |
| /payments/sandbox | Local create, authorize, capture, void, refund and status refresh |
| /payments/{provider} | Seven provider sandbox labs with credential configuration and normalized responses |
| /booking | Capacity, duration, buffer, availability, reserve, confirm, reschedule and cancel |
| /logistics | Three carrier presentations, local rates and shipment lifecycle simulation |
| /components/{component} | All 15 public components, configurable fixtures, callbacks and full source |
| /architecture | Dependency direction and component replacement guidance |

The application's Instructions tabs are the maintained workshop guides. Package READMEs remain API references.

## Safety and persistence

All demo stores and local providers are scoped to one Blazor circuit. They do not share carts, payments, inventory or booking data with other visitors. Navigating within that circuit retains server-side stores; a new session starts with fresh data.

The storefront always uses the local payment driver. Logistics is a local simulation even when a carrier name is selected. External payment labs require test credentials. Known live Stripe/Tap key prefixes and invalid numeric settings are rejected; not all gateways identify the environment in their credential format, so use provider-issued sandbox accounts only.

Credentials stay in server memory and expire with the circuit, including its reconnect retention window. Clear keys explicitly disconnects a provider. Development mode may seed credentials from user-secrets or configuration; production mode does not expose machine-configured accounts to visitors. Outbound provider clients have a 30-second timeout and do not follow redirects. Raw provider errors and client secrets are not rendered.

Provider SDK card entry, webhook receivers and payment return pages are not implemented by this workshop. The relevant Instructions tab explains these boundaries and links to official provider setup and test data.

## Maintain and verify

Styles are authored in src/css/demo.less and browser helpers in src/js/demo.js. Never edit generated CSS directly. The asset build is compatible with the existing CloudMate configuration.

~~~powershell
npm ci
npm run build:demo-assets
dotnet test CloudCommerce.slnx
dotnet publish CloudCommerce.Demo --configuration Release --output artifacts/demo-host
npx playwright install chromium
npm run test:demo-browser
~~~

The browser checks start their own production-mode local host, use only local fixtures, and block external network requests. CI runs the same checks and verifies generated assets. Source exports are embedded at build time from an explicit source list; configuration files and runtime secrets are excluded.

CloudCommerce Demo is part of [Angry Monkey Cloud](https://angrymonkeycloud.com). Development follows the shared [AI instructions](https://github.com/angrymonkeycloud/CloudDocs/blob/main/docs/ai/instructions.md). See the [GitHub organization](https://github.com/angrymonkeycloud).
