using System.Collections.Concurrent;
using System.Reflection;

namespace AngryMonkey.CloudCommerce.Demo;

public sealed record DemoInstruction(string Title, string Description);
public sealed record DemoSourceFile(string Name, string Description, string Content);
public sealed record DemoGuide(string Title, string Summary, string Setup, IReadOnlyList<DemoInstruction> Steps, string Expected, string Troubleshooting, IReadOnlyList<DemoSourceFile> Files, bool HasConfiguration = false, ComponentReferenceDefinition? Component = null, PaymentProviderLabDefinition? Provider = null);

public static class DemoWorkshopCatalog
{
    private static readonly ConcurrentDictionary<string, string> Sources = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, DemoGuide> Guides = new(StringComparer.OrdinalIgnoreCase);
    private const string RunSetup = "Install the .NET 10 SDK, clone CloudCommerce, and run dotnet run --project CloudCommerce.Demo. Use the local HTTPS address printed by the application. The View tab runs the demo; the Code tab contains copyable source and the Instructions tab stays beside it.";
    private const string SourceNote = "Complete source from this build. Demo files use the CloudCommerce.Demo namespace, fixtures and shared workshop UI; copy their dependencies too, or adapt the integration example to your application.";

    public static DemoGuide Find(string path)
    {
        string normalized = path.Trim('/').ToLowerInvariant();
        if (normalized.StartsWith("components/") && ComponentReferenceCatalog.Find(normalized[11..]) is { } component)
            return Guides.GetOrAdd(normalized, _ => Component(component));
        if (normalized.StartsWith("payments/") && PaymentProviderLabCatalog.Find(normalized[9..]) is { } provider)
            return Guides.GetOrAdd(normalized, _ => Payment(provider));
        string key = normalized is "" or "components" or "payments" or "booking" or "logistics" or "storefront" or "architecture" ? normalized : "";
        return Guides.GetOrAdd(key, Journey);
    }

    public static string ReadSource(string name) => Sources.GetOrAdd(name, key =>
    {
        Assembly assembly = typeof(DemoWorkshopCatalog).Assembly;
        using Stream stream = assembly.GetManifestResourceStream($"Workshop.{key}") ?? throw new InvalidOperationException($"Workshop source is missing: {key}");
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    });

    private static DemoSourceFile Source(string name) => new(name, SourceNote, ReadSource(name));
    private static DemoSourceFile Snippet(string name, string content) => new(name, "Integration excerpt. Register the packages and supply the application-owned models described in Instructions; the complete demo source is also available in this menu.", content);
    private static IReadOnlyList<DemoSourceFile> Files(string page, string snippet) =>
        [Snippet("Integration.cs", snippet), Source($"{page}.razor"), Source("Program.cs"), Source("DemoSessionServices.cs"), Source("_Imports.razor"), Source("DemoCatalog.cs"), Source("DemoProviders.cs")];

    private static DemoGuide Journey(string path) => path switch
    {
        "storefront" => new("Storefront", "Compose a catalog, discounts, delivery, booking and payment in one safe local checkout.", RunSetup,
            [new("Choose your features", "Use Configuration to enable delivery and appointment booking. Delivery changes the checkout total; booking appears when the Architecture Studio service is in the cart. Feature changes are locked during payment and after completion."),
             new("Build the cart", "Add offerings in View. Add the same item twice to increase its quantity, then review the cart. Remove a row to remove that offering. Apply MONKEY20 for a 20% discount; other codes have no demo adjustment."),
             new("Complete checkout", "Continue to checkout, choose delivery and a session if enabled, then pick a payment presentation. Pay runs the local Demo driver, even when a real provider name is selected."),
             new("Inspect and repeat", "Compare the order amount with the summary, copy the order reference, and start another journey. Confirmed booking capacity remains occupied during the session.")],
            "The final order contains normalized payment state and the selected inventory and booking reservations. No real gateway is contacted.",
            "An empty cart cannot check out. A slot can become unavailable after reservation; choose another if capacity is exhausted. In-memory data resets with a new session. Production requires durable stores, server-owned pricing and verified payment completion.",
            Files("Storefront", DemoCodeSamples.Storefront), true),
        "booking" => new("Booking", "Explore availability, duration, buffers, capacity and the entire reservation lifecycle.", RunSetup,
            [new("Configure availability", "In Configuration choose resource capacity, session length and buffer, then apply. Existing active reservations must be cancelled before changing the scenario."),
             new("Reserve capacity", "Select a UTC slot and reserve it. Availability immediately reflects the occupied capacity and buffer."),
             new("Confirm or reschedule", "Confirm the pending reservation. Select another slot and reschedule to move the reservation and release the original time."),
             new("Cancel and repeat", "Cancel a pending or confirmed reservation. The capacity returns to availability. Book another session to verify that release.")],
            "Status moves from Pending to Confirmed or Cancelled. Rescheduling retains the reservation identity and changes its time.",
            "Slots are expressed in UTC. A 15-minute buffer can remove adjacent starts. There is no payment requirement here. In production, configure an application-owned resource and persistent IBookingStore.",
            Files("Booking", DemoCodeSamples.Booking), true),
        "logistics" => new("Logistics", "Compare carrier presentations and exercise a clearly labeled local shipment simulation.", RunSetup,
            [new("Select a carrier", "Choose Aramex, DHL Express or FedEx. The right panel controls service level and tracking-event visibility."),
             new("Choose a rate", "Select economy, priority or same-day. Prices and inventory shown here are fixed fixtures, not carrier quotes."),
             new("Advance delivery", "Create the label, hand the parcel to the carrier, move to the final mile and confirm delivery. Observe the normalized shipment status at every step."),
             new("Restart or integrate", "Restart the journey, or use the Code tab to register a real adapter and request rates. Supply validated addresses, parcel dimensions and carrier-issued test credentials in your application.")],
            "The tracker progresses Pending → LabelCreated → InTransit → OutForDelivery → Delivered. The demo never purchases a label or contacts a carrier.",
            "Changing the service restarts the simulated shipment. Carrier availability, rates and labels require a real sandbox integration; a configured adapter does not make this simulation a live carrier test.",
            Files("Logistics", DemoCodeSamples.Logistics), true),
        "architecture" => new("Architecture", "Understand package dependencies and decide what your application needs.", RunSetup,
            [new("Choose a domain", "Payments, logistics and booking are independently usable. Start with one domain and add its provider package."),
             new("Compose when needed", "Add CloudCommerce only to coordinate pricing, inventory, booking and payments across a checkout."),
             new("Replace the UI", "Use shipped components directly, or register a replacement through CommerceComponentHost. Visit the host demo to inspect the contract.")],
            "Specialized domains depend on their own contracts; application code owns product meaning, persistence and customer experience.",
            "Demo fixtures, providers and workshop UI are application code and do not ship in the public component package.",
            [.. Files("Architecture", DemoCodeSamples.Architecture), Snippet("ComponentOverrides.cs", DemoCodeSamples.Overrides)]),
        "components" => new("Component catalog", "Every shipped Razor component has a live reference, full source and instructions.", RunSetup,
            [new("Open a component", "Use the left navigation or a catalog card. Each component has its own stable route."),
             new("Exercise its inputs", "Change the configuration panel, trigger callbacks and inspect the event log."),
             new("Copy and integrate", "Open Code for the complete implementation and working demo fixtures. Instructions lists public parameters and callbacks.")],
            "All 15 shipped components, including composition hosts and cart rows, are directly reachable.",
            "Package components render application-owned data. The demo's configuration panels and event inspector are not part of the NuGet package.",
            Files("ComponentGallery", DemoCodeSamples.Overrides)),
        "payments" => new("Payment providers", "Start locally, then run a dedicated provider sandbox with your own test credentials.", RunSetup,
            [new("Start without credentials", "Open Built-in sandbox to explore create, authorize, capture, void and refund."),
             new("Choose a real provider", "Open the provider's dedicated lab from the left navigation. Instructions lists required fields and official test resources."),
             new("Configure and run", "Add test credentials in the right panel, set amount and currency, and submit. Inspect status and hosted next actions in View.")],
            "The built-in adapter is local. Provider labs make real sandbox network requests only after credentials are configured.",
            "Never use live credentials. Some gateways use shared production/test API hosts, so test-issued credentials are essential. Sandbox availability and account activation are controlled by the provider.",
            Files("Payments", DemoCodeSamples.Composition)),
        _ => new("Workshop overview", "This demo application is the single place to learn, inspect code, configure scenarios and run the CloudCommerce ecosystem.", RunSetup,
            [new("Choose a demo", "The left navigation includes complete journeys, every payment provider and every public component."),
             new("Try, inspect, learn", "View runs the scenario. Code includes downloadable source from the same build. Instructions explains setup, actions, expected results and troubleshooting."),
             new("Configure the scenario", "Open the right panel on interactive demos to turn features on or off, adjust fixtures or connect sandbox credentials. Tabs preserve the current scenario; navigating to another demo starts its page state again.")],
            "You can complete a storefront, manage a reservation and explore payment lifecycles without third-party credentials.",
            "Local simulations are labeled. Real provider sandboxes need test accounts and network access. No production deployment is implied by running a demo.",
            Files("Home", DemoCodeSamples.Composition))
    };

    private static DemoGuide Component(ComponentReferenceDefinition component) => new(component.Title, component.Description,
        "Install AngryMonkey.CloudCommerce.Components, call AddCloudCommerceComponents(), import AngryMonkey.CloudCommerce.Components.Components, and load _content/AngryMonkey.CloudCommerce.Components/css/cloud-commerce.css in an interactive Blazor application.",
        [new("Configure the fixture", "Use the right panel to change the fixture, quantity or empty state. Reset restores the initial scenario."),
         new("Try the component", component.Slug switch
         {
             "product-card" or "product-information" => "Choose a product, press Add, and inspect the emitted ProductPresentation ID.",
             "cart-view" or "cart-item-view" => "Change quantity and remove a row. The demo updates the cart and records its OnRemove ID. Reset restores removed items.",
             "coupon-input" => "Enter MONKEY20 and submit. The inspector records the submitted code; applying pricing rules belongs to your application.",
             "payment-selector" => "Choose a provider. The selected state and OnSelected provider name update together. This component does not create payments.",
             "shipping-selector" => "Choose a service and inspect its provider/service code. Try the empty fixture to verify unavailable options.",
             "booking-selector" => "Choose a UTC date and slot. The callback emits a TimeSlot. Use the Booking demo for reservations and capacity.",
             "search-filter" => "Type a query and clear it. QueryChanged reports the current query on each input.",
             "provider-card" => "Select the provider card to inspect OnSelected. Its configured state comes from the fixture, not a network check.",
             "checkout-progress" => "Change the active step in Configuration and observe completed and current indicators.",
             "shipment-tracker" => "Change shipment status and show or hide event data. Links and dates belong to the supplied shipment.",
             "order-summary" or "checkout-view" => "Change fixture quantity and inspect totals. CheckoutView also hosts the supplied child content and progress steps.",
             _ => "Choose the ProductCard or OrderSummary slot in Configuration. CommerceComponentHost resolves the registered component and supplies its parameters."
         }),
         new("Use the public API", "Copy the component source or complete reference page from Code. Supply the listed parameters and handle callbacks in your application; no demo wrapper is required.")],
        component.Events.Count > 0 ? "The live component updates and the event inspector shows its latest callback. Changing tabs preserves the same instance." : "The rendered output follows the supplied model. This display component does not perform a domain operation.",
        "An empty fixture intentionally removes options or rows. The Code menu includes actual parameter types and defaults. For real pricing, booking or payment operations, use the corresponding domain service on the server.",
        [Source($"{component.Tag}.razor"), Source("ComponentDetail.razor"), Source("DemoCatalog.cs"), Source("_Imports.razor"), Snippet("Registration.cs", DemoCodeSamples.Composition)], true, component);

    private static DemoGuide Payment(PaymentProviderLabDefinition provider) => new(provider.DisplayName, provider.CustomerExperience,
        provider.RequiresCredentials ? "Create a provider test account using the official links below. Paste only sandbox credentials into Configuration and connect. Credentials stay in the server-side circuit and are cleared explicitly or when that circuit expires; closing a tab may leave a reconnect window." : RunSetup,
        [new("Configure the request", provider.RequiresCredentials ? "Connect the required keys in the right panel. Set a positive amount and a three-letter currency. SkipCash requires QAR. Clear keys disconnects this session." : "No keys are required. Select Create or Authorize in the right panel; set amount and currency."),
         new("Send a payment", "Press Pay once and inspect the returned ID, amount, status and elapsed time. Each new request has its own idempotency key."),
         new("Complete the next action", provider.RequiresCredentials ? "Open the provider's HTTPS hosted checkout when returned and use its official test data. Stripe and some Adyen flows need the official client SDK; this lab creates the intent but does not mount those SDKs." : "Capture or void an Authorized payment. Refund a Captured payment. These actions use the in-memory provider and make no network calls."),
         new("Refresh and inspect", "Refresh status to query the same adapter. Copy registration and request examples from Code; session credentials are never included in copied files.")],
        "The response displays normalized state. Authentication, unsupported currencies and unavailable sandboxes produce a recoverable error. A returned intent or redirect does not prove payment completion.",
        "Only development mode loads machine-configured secrets; public hosts require per-session credentials. Known live-key prefixes are rejected, but not every gateway encodes its environment in its key. Use test accounts. Provider return URLs and webhook handling must be implemented by the consuming application; no webhook receiver or hosted checkout completion page is provided here.",
        [Snippet("Program.registration.cs", provider.RegistrationCode), Snippet("CreatePayment.cs", provider.RequestCode), Snippet("Setup.txt", provider.RequiresCredentials ? PaymentProviderLabCatalog.ConfigurationCommands(provider) : "No credentials required."), Source("PaymentProviderLab.razor"), Source("SandboxPaymentRunner.cs"), Source("ProviderCredentials.cs"), Source("DemoProviders.cs")], true, Provider: provider);
}
