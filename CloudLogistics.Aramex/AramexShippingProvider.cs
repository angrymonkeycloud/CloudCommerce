using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using AngryMonkey.Cloud.Geography;

namespace AngryMonkey.CloudLogistics.Aramex;

public sealed class AramexShippingProvider(HttpClient httpClient, AramexOptions options) : IShippingProvider
{
    private readonly ConcurrentDictionary<Guid, Shipment> _shipments = new();

    public string Name => "Aramex";
    public ShippingProviderCapabilities Capabilities => ShippingProviderCapabilities.Rates | ShippingProviderCapabilities.Labels | ShippingProviderCapabilities.Tracking;

    public async Task<IReadOnlyList<ShippingRate>> GetRatesAsync(ShippingRateRequest request, CancellationToken cancellationToken = default)
    {
        EnsurePackages(request.Packages);
        object body = new
        {
            ClientInfo = ClientInfo(),
            OriginAddress = Address(request.Origin),
            DestinationAddress = Address(request.Destination),
            ShipmentDetails = ShipmentDetails(request.Packages, request.Metadata?.GetValueOrDefault("productGroup") ?? "EXP", request.Metadata?.GetValueOrDefault("productType") ?? "PDX")
        };
        JsonElement root = await PostAsync("ShippingAPI.V2/RateCalculator/Service_1_0.svc/json/CalculateRate", body, cancellationToken);
        ThrowForNotifications(root);
        JsonElement total = root.GetProperty("TotalAmount");
        decimal value = total.GetProperty("Value").GetDecimal();
        string currency = total.GetProperty("CurrencyCode").GetString()!;
        string serviceCode = request.Metadata?.GetValueOrDefault("productType") ?? "PDX";
        return [new(Name, serviceCode, $"Aramex {serviceCode}", new Money(currency, value))];
    }

    public async Task<Shipment> CreateShipmentAsync(ShipmentRequest request, CancellationToken cancellationToken = default)
    {
        EnsurePackages(request.Packages);
        object shipment = new
        {
            Reference1 = request.Reference,
            Shipper = Party(request.Origin),
            Consignee = Party(request.Destination),
            ShippingDateTime = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            Details = ShipmentDetails(request.Packages, "EXP", request.ServiceCode)
        };
        object body = new
        {
            ClientInfo = ClientInfo(),
            LabelInfo = new { ReportID = options.LabelReportId, ReportType = "URL" },
            Shipments = new[] { shipment }
        };

        JsonElement root = await PostAsync("ShippingAPI.V2/Shipping/Service_1_0.svc/json/CreateShipments", body, cancellationToken);
        ThrowForNotifications(root);
        JsonElement result = root.GetProperty("Shipments")[0];
        string trackingNumber = result.GetProperty("ID").GetString()!;
        Uri? labelUrl = result.TryGetProperty("ShipmentLabel", out JsonElement label) && label.TryGetProperty("LabelURL", out JsonElement labelValue) && Uri.TryCreate(labelValue.GetString(), UriKind.Absolute, out Uri? parsedLabel) ? parsedLabel : null;
        Guid id = Guid.NewGuid();
        Shipment created = new(id, Name, request.ServiceCode, ShipmentStatuses.LabelCreated, trackingNumber, TrackingUrl(trackingNumber), labelUrl, ProviderReference: trackingNumber);
        _shipments[id] = created;
        return created;
    }

    public Task<Shipment?> GetShipmentAsync(Guid shipmentId, CancellationToken cancellationToken = default)
    {
        _shipments.TryGetValue(shipmentId, out Shipment? shipment);
        return Task.FromResult(shipment);
    }

    public async Task<Shipment> RefreshTrackingAsync(Guid shipmentId, CancellationToken cancellationToken = default)
    {
        Shipment current = await GetShipmentAsync(shipmentId, cancellationToken) ?? throw new KeyNotFoundException($"Shipment '{shipmentId}' was not found.");
        string trackingNumber = current.TrackingNumber ?? throw new InvalidOperationException("The Aramex shipment has no tracking number.");
        object body = new { ClientInfo = ClientInfo(), GetLastTrackingUpdateOnly = true, Shipments = new[] { trackingNumber } };
        JsonElement root = await PostAsync("ShippingAPI.V2/Tracking/Service_1_0.svc/json/TrackShipments", body, cancellationToken);
        ThrowForNotifications(root);
        JsonElement update = root.GetProperty("TrackingResults")[0].GetProperty("Value")[0];
        string description = update.TryGetProperty("UpdateDescription", out JsonElement value) ? value.GetString() ?? string.Empty : string.Empty;
        ShipmentStatuses status = MapStatus(description);
        Shipment updated = current with { Status = status, DeliveredAt = status == ShipmentStatuses.Delivered ? DateTimeOffset.UtcNow : current.DeliveredAt };
        _shipments[shipmentId] = updated;
        return updated;
    }

    public Task CancelShipmentAsync(Guid shipmentId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Aramex shipment cancellation is not exposed by this adapter. Pickup cancellation is a separate carrier operation.");

    public Task<IReadOnlyList<PickupLocation>> GetPickupLocationsAsync(string countryCode, string? subdivisionCode = null, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<PickupLocation>>([]);

    private async Task<JsonElement> PostAsync(string path, object body, CancellationToken cancellationToken)
    {
        options.Validate();
        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(path, body, cancellationToken);
        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private object ClientInfo() => new
    {
        options.UserName,
        options.Password,
        Version = "v1.0",
        options.AccountNumber,
        options.AccountPin,
        options.AccountEntity,
        options.AccountCountryCode,
        Source = 24
    };

    private static object Address(LogisticsAddress address) => new
    {
        Line1 = address.Line1,
        Line2 = address.Line2,
        Line3 = string.Empty,
        City = address.Locality,
        StateOrProvinceCode = address.SubdivisionCode,
        PostCode = address.PostalCode,
        CountryCode = address.CountryCode
    };

    private static object Party(LogisticsAddress address) => new
    {
        Reference1 = string.Empty,
        AccountNumber = string.Empty,
        PartyAddress = Address(address),
        Contact = new { Department = string.Empty, PersonName = address.RecipientName ?? string.Empty, Title = string.Empty, CompanyName = address.RecipientName ?? string.Empty, PhoneNumber1 = address.PhoneNumber ?? string.Empty, PhoneNumber1Ext = string.Empty, PhoneNumber2 = string.Empty, PhoneNumber2Ext = string.Empty, FaxNumber = string.Empty, CellPhone = address.PhoneNumber ?? string.Empty, EmailAddress = string.Empty, Type = string.Empty }
    };

    private static object ShipmentDetails(IReadOnlyList<ShippingPackage> packages, string productGroup, string productType)
    {
        decimal totalWeight = packages.Sum(package => package.Weight);
        return new
        {
            Dimensions = new { Length = packages.Max(package => package.Length), Width = packages.Max(package => package.Width), Height = packages.Sum(package => package.Height), Unit = packages[0].LengthUnit.ToUpperInvariant() },
            ActualWeight = new { Value = totalWeight, Unit = packages[0].WeightUnit.ToUpperInvariant() },
            ChargeableWeight = new { Value = totalWeight, Unit = packages[0].WeightUnit.ToUpperInvariant() },
            DescriptionOfGoods = "Goods",
            GoodsOriginCountry = string.Empty,
            NumberOfPieces = packages.Count,
            ProductGroup = productGroup,
            ProductType = productType,
            PaymentType = "P",
            PaymentOptions = string.Empty,
            Services = string.Empty,
            Items = Array.Empty<object>()
        };
    }

    private static void EnsurePackages(IReadOnlyList<ShippingPackage> packages)
    {
        if (packages.Count == 0 || packages.Any(package => package.Weight <= 0 || package.Length <= 0 || package.Width <= 0 || package.Height <= 0))
            throw new ArgumentException("At least one package with positive dimensions and weight is required.", nameof(packages));
    }

    private static void ThrowForNotifications(JsonElement root)
    {
        bool hasErrors = root.TryGetProperty("HasErrors", out JsonElement errors) && errors.ValueKind == JsonValueKind.True;
        if (!hasErrors)
            return;

        string message = root.TryGetProperty("Notifications", out JsonElement notifications)
            ? string.Join("; ", notifications.EnumerateArray().Select(notification => notification.TryGetProperty("Message", out JsonElement value) ? value.GetString() : "Aramex error"))
            : "Aramex request failed.";
        throw new InvalidOperationException(message);
    }

    private static ShipmentStatuses MapStatus(string description) => description.ToLowerInvariant() switch
    {
        string value when value.Contains("delivered") => ShipmentStatuses.Delivered,
        string value when value.Contains("out for delivery") => ShipmentStatuses.OutForDelivery,
        string value when value.Contains("return") => ShipmentStatuses.Returned,
        string value when value.Contains("fail") || value.Contains("undeliver") => ShipmentStatuses.DeliveryFailed,
        _ => ShipmentStatuses.InTransit
    };

    private static Uri TrackingUrl(string trackingNumber) => new($"https://www.aramex.com/track/results?ShipmentNumber={Uri.EscapeDataString(trackingNumber)}");
}
