namespace AngryMonkey.CloudCommerce.Components;

public sealed record PaymentProviderOption(
    string Name,
    string DisplayName,
    string Description,
    string? Badge = null,
    string? Hint = null,
    bool IsRecommended = false,
    bool IsConfigured = true);

public sealed record ProviderCardModel(
    string Name,
    string Category,
    string Description,
    string Region,
    IReadOnlyList<string> Capabilities,
    Uri DocumentationUrl,
    Uri? TestingUrl = null,
    Uri? PortalUrl = null,
    bool IsConfigured = false,
    string Accent = "violet");

public sealed record CheckoutStepModel(string Label, string Description, bool IsComplete = false, bool IsCurrent = false);

public sealed record ShipmentTrackingEvent(string Label, string Description, DateTimeOffset? OccurredAt = null, bool IsComplete = false, bool IsCurrent = false);