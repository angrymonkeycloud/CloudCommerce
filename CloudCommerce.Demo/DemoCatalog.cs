using AngryMonkey.Cloud.Geography;
using AngryMonkey.CloudCommerce;
using AngryMonkey.CloudCommerce.Components;

namespace AngryMonkey.CloudCommerce.Demo;

public static class DemoCatalog
{
    public static readonly Guid WarehouseId = Guid.Parse("98da8de4-50b9-4c61-a4f9-b6f6ed61f75b");
    public static readonly Guid ResourceId = Guid.Parse("cb9880b3-864f-497f-8667-f81e9b32291d");

    public static IReadOnlyList<ProductPresentation> Products { get; } =
    [
        new("cloud-essentials", "Cloud Essentials Kit", new Money("USD", 48m), "A tactile starter kit for teams designing their first connected commerce experience.", Reference: "kit-essentials", Metadata: new() { ["category"] = "Physical", ["accent"] = "violet" }),
        new("architecture-session", "Architecture Studio", new Money("USD", 120m), "A 60-minute commerce architecture session with a specialist and a written boundary map.", Reference: "architecture-session", Metadata: new() { ["category"] = "Bookable", ["accent"] = "cyan" }),
        new("regional-playbook", "MENA Launch Playbook", new Money("USD", 34m), "An application-owned digital guide to payment, delivery, tax, and localization decisions.", Reference: "regional-playbook", Metadata: new() { ["category"] = "Digital", ["accent"] = "amber" })
    ];

    public static IReadOnlyList<ProviderCardModel> PaymentProviders { get; } =
    [
        new("Stripe", "Payment provider", "Global card and wallet orchestration with authorization, capture, refunds, recurring references, and signed events.", "Global", ["Authorize", "Capture", "Refund", "Recurring", "Webhooks"], new("https://docs.stripe.com/payments"), new("https://docs.stripe.com/testing"), new("https://dashboard.stripe.com/test/apikeys"), Accent: "violet"),
        new("PayPal", "Payment provider", "PayPal wallet and card checkout through sandbox accounts, order authorization, capture, and refunds.", "Global", ["Wallet", "Cards", "Authorize", "Capture", "Refund"], new("https://developer.paypal.com/api/rest/"), new("https://developer.paypal.com/sandbox-testing/card-testing"), new("https://developer.paypal.com/dashboard/"), Accent: "blue"),
        new("Adyen", "Enterprise payments", "Unified enterprise payment processing with test-platform credentials, modifications, and webhook verification.", "Global", ["Payments", "Capture", "Cancel", "Refund", "Webhooks"], new("https://docs.adyen.com/online-payments/"), new("https://docs.adyen.com/development-resources/test-cards-and-credentials/test-card-numbers"), new("https://ca-test.adyen.com/"), Accent: "green"),
        new("MyFatoorah", "Regional payments", "Hosted payment execution for MENA markets with invoice lookup, refunds, capture or release, and signed webhooks.", "MENA", ["Hosted checkout", "KNET", "Refund", "Capture", "Webhooks"], new("https://docs.myfatoorah.com/docs/get-started"), new("https://docs.myfatoorah.com/docs/test-cards"), new("https://portal.myfatoorah.com/"), Accent: "cyan"),
        new("SkipCash", "Qatar payments", "QAR-first hosted checkout with ordered HMAC request signing, transaction lookup, refund status, and callbacks.", "Qatar", ["Hosted checkout", "QAR", "Lookup", "Refund status", "Webhooks"], new("https://skipcash.app/assets/doc/SkipCashIntegrationManual.pdf"), new("https://skipcash.app/assets/doc/SkipCashIntegrationManual.pdf"), new("https://merchantportal.skipcash.app/"), Accent: "rose"),
        new("Tap Payments", "Regional payments", "Regional card and alternative-payment orchestration with charge, authorize, capture, void, refund, and token flows.", "MENA", ["Charge", "Authorize", "Capture", "Refund", "Saved cards"], new("https://developers.tap.company/docs/get-started"), new("https://developers.tap.company/reference/testing-cards"), new("https://business.tap.company/"), Accent: "amber"),
        new("PayTabs", "Regional payments", "Hosted checkout and transaction management for sale, authorization, capture, void, refund, query, and signed callbacks.", "MENA", ["Hosted page", "Sale", "Authorize", "Refund", "Callbacks"], new("https://support.paytabs.com/en/support/solutions/articles/60000992876-3-2-1-hosted-payment-page-apis-initiating-the-payment"), new("https://support.paytabs.com/en/support/solutions/articles/60000712315-what-are-the-test-cards-available-to-perform-payments-"), new("https://merchant.paytabs.com/"), Accent: "blue")
    ];

    public static IReadOnlyList<ProviderCardModel> ShippingProviders { get; } =
    [
        new("Aramex", "Carrier adapter", "Regional rating, shipment creation, label generation, tracking, and pickup-ready workflows.", "MENA + global", ["Rates", "Shipments", "Labels", "Tracking", "Pickup"], new("https://www.aramex.com/docs/default-source/resourses/resourcesdata/shipping-services-api-manual.pdf"), new("https://www.aramex.com/docs/default-source/resourses/resourcesdata/shipping-services-api-manual.pdf"), new("https://www.aramex.com/"), Accent: "rose"),
        new("DHL Express", "Carrier adapter", "Time-definite international delivery through MyDHL test and production environments.", "Global", ["Rates", "Products", "Shipments", "Tracking", "Documents"], new("https://developer.dhl.com/api-reference/dhl-express-mydhl-api"), new("https://developer.dhl.com/api-reference/dhl-express-mydhl-api"), new("https://developer.dhl.com/"), Accent: "amber"),
        new("FedEx", "Carrier adapter", "OAuth-backed rates, shipments, labels, tracking, and sandbox-virtualized test scenarios.", "Global", ["OAuth", "Rates", "Shipments", "Labels", "Tracking"], new("https://developer.fedex.com/api/en-us/get-started.html"), new("https://developer.fedex.com/api/en-us/get-started/shipper.html"), new("https://developer.fedex.com/"), Accent: "violet")
    ];

    public static IReadOnlyList<PaymentProviderOption> CheckoutPaymentOptions { get; } = PaymentProviders
        .Select(provider => new PaymentProviderOption(provider.Name, provider.Name, provider.Region, provider.Region == "MENA" ? "Regional" : null, "Local simulation; use provider labs for sandbox payments", provider.Name == "MyFatoorah", provider.IsConfigured))
        .ToArray();
}
