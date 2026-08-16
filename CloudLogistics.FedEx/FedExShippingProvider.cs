using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AngryMonkey.Cloud.Geography;

namespace AngryMonkey.CloudLogistics.FedEx;

public sealed class FedExShippingProvider(HttpClient httpClient, FedExOptions options) : IShippingProvider
{
    private readonly ConcurrentDictionary<Guid, Shipment> _shipments = new();
    private readonly SemaphoreSlim _tokenGate = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;

    public string Name => "FedEx";
    public ShippingProviderCapabilities Capabilities => ShippingProviderCapabilities.Rates | ShippingProviderCapabilities.Labels | ShippingProviderCapabilities.Tracking | ShippingProviderCapabilities.Cancellation;

    public async Task<IReadOnlyList<ShippingRate>> GetRatesAsync(ShippingRateRequest request, CancellationToken cancellationToken = default)
    {
        ValidatePackages(request.Packages);
        object body = new
        {
            accountNumber = new { value = options.AccountNumber },
            rateRequestControlParameters = new { returnTransitTimes = true },
            requestedShipment = new
            {
                shipper = new { address = Address(request.Origin) },
                recipient = new { address = Address(request.Destination) },
                pickupType = "DROPOFF_AT_FEDEX_LOCATION",
                rateRequestType = new[] { "ACCOUNT", "LIST" },
                requestedPackageLineItems = request.Packages.Select(package => Package(package)).ToArray()
            }
        };

        JsonElement root = await SendAsync(HttpMethod.Post, "rate/v1/rates/quotes", body, cancellationToken);
        JsonElement details = root.GetProperty("output").GetProperty("rateReplyDetails");
        List<ShippingRate> rates = [];
        foreach (JsonElement detail in details.EnumerateArray())
        {
            string code = detail.GetProperty("serviceType").GetString()!;
            string name = detail.TryGetProperty("serviceName", out JsonElement serviceName) ? serviceName.GetString() ?? code : code;
            if (!detail.TryGetProperty("ratedShipmentDetails", out JsonElement rated) || rated.GetArrayLength() == 0)
                continue;
            JsonElement charge = rated[0].GetProperty("totalNetCharge");
            Money price = new(charge.GetProperty("currency").GetString()!, charge.GetProperty("amount").GetDecimal());
            rates.Add(new(Name, code, name, price));
        }
        return rates;
    }

    public async Task<Shipment> CreateShipmentAsync(ShipmentRequest request, CancellationToken cancellationToken = default)
    {
        ValidatePackages(request.Packages);
        object body = new
        {
            labelResponseOptions = "LABEL",
            accountNumber = new { value = options.AccountNumber },
            requestedShipment = new
            {
                shipDatestamp = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                serviceType = request.ServiceCode,
                packagingType = "YOUR_PACKAGING",
                pickupType = "DROPOFF_AT_FEDEX_LOCATION",
                shipper = Party(request.Origin),
                recipients = new[] { Party(request.Destination) },
                shippingChargesPayment = new { paymentType = "SENDER" },
                labelSpecification = new { imageType = "PDF", labelStockType = "PAPER_4X6" },
                requestedPackageLineItems = request.Packages.Select(package => Package(package)).ToArray()
            }
        };

        JsonElement root = await SendAsync(HttpMethod.Post, "ship/v1/shipments", body, cancellationToken);
        JsonElement transaction = root.GetProperty("output").GetProperty("transactionShipments")[0];
        string trackingNumber = transaction.GetProperty("masterTrackingNumber").GetString()!;
        List<ShipmentDocument> documents = [];
        if (transaction.TryGetProperty("pieceResponses", out JsonElement pieces))
            foreach (JsonElement piece in pieces.EnumerateArray())
                if (piece.TryGetProperty("packageDocuments", out JsonElement packageDocuments))
                    foreach (JsonElement document in packageDocuments.EnumerateArray())
                        documents.Add(new("label", "PDF", document.TryGetProperty("encodedLabel", out JsonElement content) ? content.GetString() : null, document.TryGetProperty("url", out JsonElement url) && Uri.TryCreate(url.GetString(), UriKind.Absolute, out Uri? parsed) ? parsed : null));

        Guid id = Guid.NewGuid();
        Shipment shipment = new(id, Name, request.ServiceCode, ShipmentStatuses.LabelCreated, trackingNumber, new Uri($"https://www.fedex.com/fedextrack/?trknbr={Uri.EscapeDataString(trackingNumber)}"), ProviderReference: trackingNumber, Documents: documents);
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
        string trackingNumber = current.TrackingNumber ?? throw new InvalidOperationException("The FedEx shipment has no tracking number.");
        object body = new { includeDetailedScans = false, trackingInfo = new[] { new { trackingNumberInfo = new { trackingNumber } } } };
        JsonElement root = await SendAsync(HttpMethod.Post, "track/v1/trackingnumbers", body, cancellationToken);
        JsonElement result = root.GetProperty("output").GetProperty("completeTrackResults")[0].GetProperty("trackResults")[0];
        string code = result.GetProperty("latestStatusDetail").GetProperty("code").GetString() ?? string.Empty;
        ShipmentStatuses status = code.ToUpperInvariant() switch
        {
            "DL" => ShipmentStatuses.Delivered,
            "OD" => ShipmentStatuses.OutForDelivery,
            "DE" or "SE" => ShipmentStatuses.DeliveryFailed,
            "RS" => ShipmentStatuses.Returned,
            _ => ShipmentStatuses.InTransit
        };
        Shipment updated = current with { Status = status, DeliveredAt = status == ShipmentStatuses.Delivered ? DateTimeOffset.UtcNow : current.DeliveredAt };
        _shipments[shipmentId] = updated;
        return updated;
    }

    public async Task CancelShipmentAsync(Guid shipmentId, CancellationToken cancellationToken = default)
    {
        Shipment current = await GetShipmentAsync(shipmentId, cancellationToken) ?? throw new KeyNotFoundException($"Shipment '{shipmentId}' was not found.");
        string trackingNumber = current.TrackingNumber ?? throw new InvalidOperationException("The FedEx shipment has no tracking number.");
        object body = new { accountNumber = new { value = options.AccountNumber }, trackingNumber, deletionControl = "DELETE_ALL_PACKAGES" };
        await SendAsync(HttpMethod.Put, "ship/v1/shipments/cancel", body, cancellationToken);
        _shipments[shipmentId] = current with { Status = ShipmentStatuses.Cancelled };
    }

    public Task<IReadOnlyList<PickupLocation>> GetPickupLocationsAsync(string countryCode, string? subdivisionCode = null, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<PickupLocation>>([]);

    private async Task<JsonElement> SendAsync(HttpMethod method, string path, object body, CancellationToken cancellationToken)
    {
        string token = await GetAccessTokenAsync(cancellationToken);
        using HttpRequestMessage request = new(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.TryAddWithoutValidation("X-locale", "en_US");
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(ReadError(json, response.ReasonPhrase));
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is not null && _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
            return _accessToken;

        await _tokenGate.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null && _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
                return _accessToken;

            options.Validate();
            using HttpRequestMessage request = new(HttpMethod.Post, "oauth/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials", ["client_id"] = options.ClientId, ["client_secret"] = options.ClientSecret })
            };
            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            response.EnsureSuccessStatusCode();
            using JsonDocument document = JsonDocument.Parse(json);
            _accessToken = document.RootElement.GetProperty("access_token").GetString()!;
            int expiresIn = document.RootElement.TryGetProperty("expires_in", out JsonElement expires) ? expires.GetInt32() : 3600;
            _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
            return _accessToken;
        }
        finally
        {
            _tokenGate.Release();
        }
    }

    private static object Address(LogisticsAddress address) => new { streetLines = new[] { address.Line1, address.Line2 }.Where(line => !string.IsNullOrWhiteSpace(line)).ToArray(), city = address.Locality, stateOrProvinceCode = address.SubdivisionCode, postalCode = address.PostalCode, countryCode = address.CountryCode, residential = false };

    private static object Party(LogisticsAddress address) => new { contact = new { personName = address.RecipientName ?? string.Empty, phoneNumber = address.PhoneNumber ?? string.Empty, companyName = address.RecipientName ?? string.Empty }, address = Address(address) };

    private static object Package(ShippingPackage package, int index) => new { sequenceNumber = index + 1, weight = new { units = package.WeightUnit.Equals("lb", StringComparison.OrdinalIgnoreCase) ? "LB" : "KG", value = package.Weight }, dimensions = new { length = package.Length, width = package.Width, height = package.Height, units = package.LengthUnit.Equals("in", StringComparison.OrdinalIgnoreCase) ? "IN" : "CM" } };

    private static object Package(ShippingPackage package) => Package(package, 0);

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
            if (document.RootElement.TryGetProperty("errors", out JsonElement errors) && errors.GetArrayLength() > 0 && errors[0].TryGetProperty("message", out JsonElement message))
                return message.GetString() ?? fallback ?? "FedEx request failed.";
        }
        catch (JsonException) { }
        return fallback ?? "FedEx request failed.";
    }
}
