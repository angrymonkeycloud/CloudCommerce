using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AngryMonkey.Cloud.Geography;

namespace AngryMonkey.CloudLogistics.DhlExpress;

public sealed class DhlExpressShippingProvider(HttpClient httpClient, DhlExpressOptions options) : IShippingProvider
{
    private readonly ConcurrentDictionary<Guid, Shipment> _shipments = new();

    public string Name => "DHL Express";
    public ShippingProviderCapabilities Capabilities => ShippingProviderCapabilities.Rates | ShippingProviderCapabilities.Labels | ShippingProviderCapabilities.Tracking | ShippingProviderCapabilities.Cancellation;

    public async Task<IReadOnlyList<ShippingRate>> GetRatesAsync(ShippingRateRequest request, CancellationToken cancellationToken = default)
    {
        ValidatePackages(request.Packages);
        object body = new
        {
            customerDetails = new { shipperDetails = Address(request.Origin), receiverDetails = Address(request.Destination) },
            accounts = new[] { new { typeCode = "shipper", number = options.AccountNumber } },
            plannedShippingDateAndTime = DateTimeOffset.UtcNow.AddHours(1).ToString("yyyy-MM-ddTHH:mm:ss 'GMT'zzz", CultureInfo.InvariantCulture),
            unitOfMeasurement = "metric",
            isCustomsDeclarable = !request.Origin.CountryCode.Equals(request.Destination.CountryCode, StringComparison.OrdinalIgnoreCase),
            packages = request.Packages.Select(Package).ToArray()
        };
        JsonElement root = await SendAsync(HttpMethod.Post, "rates", body, cancellationToken);
        if (!root.TryGetProperty("products", out JsonElement products))
            return [];

        List<ShippingRate> rates = [];
        foreach (JsonElement product in products.EnumerateArray())
        {
            JsonElement? price = product.TryGetProperty("totalPrice", out JsonElement prices) && prices.GetArrayLength() > 0 ? prices[0] : null;
            if (price is null)
                continue;
            string currency = price.Value.GetProperty("priceCurrency").GetString()!;
            decimal amount = price.Value.GetProperty("price").GetDecimal();
            string code = product.GetProperty("productCode").GetString()!;
            string name = product.TryGetProperty("productName", out JsonElement productName) ? productName.GetString() ?? code : code;
            TimeSpan? transit = product.TryGetProperty("deliveryCapabilities", out JsonElement capabilities) && capabilities.TryGetProperty("totalTransitDays", out JsonElement days) ? TimeSpan.FromDays(days.GetInt32()) : null;
            rates.Add(new(Name, code, name, new Money(currency, amount), transit));
        }
        return rates;
    }

    public async Task<Shipment> CreateShipmentAsync(ShipmentRequest request, CancellationToken cancellationToken = default)
    {
        ValidatePackages(request.Packages);
        object body = new
        {
            plannedShippingDateAndTime = DateTimeOffset.UtcNow.AddHours(1).ToString("yyyy-MM-ddTHH:mm:ss 'GMT'zzz", CultureInfo.InvariantCulture),
            pickup = new { isRequested = false },
            productCode = request.ServiceCode,
            accounts = new[] { new { typeCode = "shipper", number = options.AccountNumber } },
            customerDetails = new { shipperDetails = Party(request.Origin), receiverDetails = Party(request.Destination) },
            content = new
            {
                packages = request.Packages.Select(Package).ToArray(),
                isCustomsDeclarable = !request.Origin.CountryCode.Equals(request.Destination.CountryCode, StringComparison.OrdinalIgnoreCase),
                description = request.Reference ?? "Goods",
                unitOfMeasurement = "metric",
                incoterm = "DAP"
            },
            outputImageProperties = new { encodingFormat = "pdf", imageOptions = new[] { new { typeCode = "label", templateName = "ECOM26_84_001" } } },
            customerReferences = string.IsNullOrWhiteSpace(request.Reference) ? Array.Empty<object>() : new[] { new { value = request.Reference, typeCode = "CU" } }
        };

        JsonElement root = await SendAsync(HttpMethod.Post, "shipments", body, cancellationToken);
        string trackingNumber = root.GetProperty("shipmentTrackingNumber").GetString()!;
        List<ShipmentDocument> documents = [];
        if (root.TryGetProperty("documents", out JsonElement values))
            foreach (JsonElement document in values.EnumerateArray())
                documents.Add(new(document.TryGetProperty("typeCode", out JsonElement type) ? type.GetString() ?? "label" : "label", document.TryGetProperty("imageFormat", out JsonElement format) ? format.GetString() ?? "pdf" : "pdf", document.TryGetProperty("content", out JsonElement content) ? content.GetString() : null));

        Guid id = Guid.NewGuid();
        Shipment shipment = new(id, Name, request.ServiceCode, ShipmentStatuses.LabelCreated, trackingNumber, new Uri($"https://www.dhl.com/global-en/home/tracking.html?tracking-id={Uri.EscapeDataString(trackingNumber)}"), ProviderReference: trackingNumber, Documents: documents);
        _shipments[id] = shipment;
        return shipment;
    }

    public Task<Shipment?> GetShipmentAsync(Guid shipmentId, CancellationToken cancellationToken = default)
    {
        _shipments.TryGetValue(shipmentId, out Shipment? shipment);
        return Task.FromResult(shipment);
    }

    public async Task<Shipment> RefreshTrackingAsync(Guid shipmentId, CancellationToken cancellationToken = default)
    {
        Shipment current = await GetShipmentAsync(shipmentId, cancellationToken) ?? throw new KeyNotFoundException($"Shipment '{shipmentId}' was not found.");
        string trackingNumber = current.TrackingNumber ?? throw new InvalidOperationException("The DHL Express shipment has no tracking number.");
        JsonElement root = await SendAsync(HttpMethod.Get, $"shipments/{Uri.EscapeDataString(trackingNumber)}/tracking", null, cancellationToken);
        JsonElement tracked = root.GetProperty("shipments")[0];
        string code = tracked.GetProperty("status").GetProperty("statusCode").GetString() ?? string.Empty;
        ShipmentStatuses status = code.ToLowerInvariant() switch
        {
            "delivered" => ShipmentStatuses.Delivered,
            "transit" or "in-transit" => ShipmentStatuses.InTransit,
            "out-for-delivery" => ShipmentStatuses.OutForDelivery,
            "failure" => ShipmentStatuses.DeliveryFailed,
            "returned" => ShipmentStatuses.Returned,
            _ => current.Status
        };
        Shipment updated = current with { Status = status, DeliveredAt = status == ShipmentStatuses.Delivered ? DateTimeOffset.UtcNow : current.DeliveredAt };
        _shipments[shipmentId] = updated;
        return updated;
    }

    public async Task CancelShipmentAsync(Guid shipmentId, CancellationToken cancellationToken = default)
    {
        Shipment current = await GetShipmentAsync(shipmentId, cancellationToken) ?? throw new KeyNotFoundException($"Shipment '{shipmentId}' was not found.");
        string trackingNumber = current.TrackingNumber ?? throw new InvalidOperationException("The DHL Express shipment has no tracking number.");
        await SendAsync(HttpMethod.Delete, $"shipments/{Uri.EscapeDataString(trackingNumber)}", null, cancellationToken);
        _shipments[shipmentId] = current with { Status = ShipmentStatuses.Cancelled };
    }

    public Task<IReadOnlyList<PickupLocation>> GetPickupLocationsAsync(string countryCode, string? subdivisionCode = null, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<PickupLocation>>([]);

    private async Task<JsonElement> SendAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        options.Validate();
        using HttpRequestMessage request = new(method, path);
        string credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.UserName}:{options.Password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null)
            request.Content = JsonContent.Create(body);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(ReadError(json, response.ReasonPhrase));
        if (string.IsNullOrWhiteSpace(json))
            return JsonSerializer.SerializeToElement(new { });
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static object Address(LogisticsAddress address) => new { postalCode = address.PostalCode, cityName = address.Locality, countryCode = address.CountryCode, provinceCode = address.SubdivisionCode, addressLine1 = address.Line1, addressLine2 = address.Line2 };

    private static object Party(LogisticsAddress address) => new
    {
        postalAddress = Address(address),
        contactInformation = new { email = string.Empty, phone = address.PhoneNumber ?? string.Empty, mobilePhone = address.PhoneNumber ?? string.Empty, companyName = address.RecipientName ?? string.Empty, fullName = address.RecipientName ?? string.Empty }
    };

    private static object Package(ShippingPackage package) => new { weight = package.Weight, dimensions = new { length = package.Length, width = package.Width, height = package.Height } };

    private static void ValidatePackages(IReadOnlyList<ShippingPackage> packages)
    {
        if (packages.Count == 0 || packages.Any(package => package.Weight <= 0 || package.Length <= 0 || package.Width <= 0 || package.Height <= 0))
            throw new ArgumentException("At least one package with positive dimensions and weight is required.", nameof(packages));
    }

    private static string ReadError(string json, string? fallback)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("detail", out JsonElement detail) ? detail.GetString() ?? fallback ?? "DHL Express request failed." : fallback ?? "DHL Express request failed.";
        }
        catch (JsonException)
        {
            return fallback ?? "DHL Express request failed.";
        }
    }
}
