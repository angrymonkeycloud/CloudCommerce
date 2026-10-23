using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AngryMonkey.Cloud.Geography;

namespace AngryMonkey.CloudLogistics.Wakilni;

/// <summary>
/// Wakilni, the Lebanese last-mile courier. A booking is a pickup bulk at the shop holding one delivery per recipient:
/// start the bulk, add the deliveries, end the bulk, and the deliveries become active for Wakilni to schedule.
/// Wakilni prices deliveries itself, so there are no rates; cash on delivery is collected by the driver.
/// </summary>
public sealed class WakilniShippingProvider(HttpClient httpClient, WakilniOptions options) : IShippingProvider
{
    private const int UsDollars = 1;
    private const int LebanesePounds = 2;
    private const int CollectOnDelivery = 52;
    private const int AlreadyPaid = 54;

    private static readonly string[] BookingOnlyFields = ["get_order_details", "get_barcode", "receiver_id", "receiver_location_id"];

    private static readonly (string Field, string Key)[] HandlingFlags =
    [
        ("require_signature", WakilniMetadata.RequireSignature),
        ("require_picture", WakilniMetadata.RequirePicture),
        ("try_on", WakilniMetadata.TryOn),
        ("exchange", WakilniMetadata.Exchange)
    ];

    private readonly ConcurrentDictionary<Guid, Shipment> _shipments = new();
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _token;

    public string Name => "Wakilni";
    public ShippingProviderCapabilities Capabilities => ShippingProviderCapabilities.Labels | ShippingProviderCapabilities.Tracking | ShippingProviderCapabilities.Cancellation;

    public Task<IReadOnlyList<ShippingRate>> GetRatesAsync(ShippingRateRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Wakilni prices deliveries from the pickup and delivery areas in its own back office; it has no rates API.");

    public async Task<Shipment> CreateShipmentAsync(ShipmentRequest request, CancellationToken cancellationToken = default)
    {
        WakilniBooking booking = (await CreateShipmentsAsync([request], cancellationToken))[0];
        return booking.Shipment ?? throw new InvalidOperationException(booking.Error);
    }

    /// <summary>
    /// Books several deliveries in one pickup bulk, so the driver collects them in a single visit.
    /// Every request must share the same origin. A delivery Wakilni rejects is reported on its booking; the others still go out.
    /// </summary>
    public async Task<IReadOnlyList<WakilniBooking>> CreateShipmentsAsync(IReadOnlyList<ShipmentRequest> requests, CancellationToken cancellationToken = default)
    {
        if (requests.Count == 0)
            return [];

        LogisticsAddress origin = requests[0].Origin;

        if (requests.Any(request => request.Origin != origin))
            throw new ArgumentException("Every delivery in one Wakilni bulk must share the same pickup origin.", nameof(requests));

        JsonElement started = await SendAsync(HttpMethod.Post, "api/v2/clients/start_bulk", new Dictionary<string, object?>
        {
            ["location_id"] = options.PickupLocationId,
            ["longitude"] = origin.Longitude,
            ["latitude"] = origin.Latitude,
            ["area"] = origin.Locality
        }, cancellationToken);

        string bulkId = Text(started, "bulk_id") ?? throw new InvalidOperationException("Wakilni did not return a bulk id.");
        List<WakilniBooking> bookings = [];

        foreach (ShipmentRequest request in requests)
        {
            try
            {
                JsonElement delivery = await SendAsync(HttpMethod.Post, $"api/v2/clients/add_delivery/{Uri.EscapeDataString(bulkId)}", DeliveryBody(request), cancellationToken);
                bookings.Add(new WakilniBooking(request, ToShipment(request, delivery), null));
            }
            catch (WakilniException exception)
            {
                bookings.Add(new WakilniBooking(request, null, exception.Message));
            }
        }

        if (bookings.Any(booking => booking.Shipment is not null))
            await SendAsync(HttpMethod.Post, $"api/v2/clients/end_bulk/{Uri.EscapeDataString(bulkId)}", null, cancellationToken);

        // Signature, picture, try on and exchange are only accepted when updating an order, so they follow the booking.
        for (int index = 0; index < bookings.Count; index++)
        {
            if (bookings[index].Shipment is not Shipment shipment || HandlingOptions(bookings[index].Request) is not { Count: > 0 } handling)
                continue;

            try
            {
                await SendAsync(HttpMethod.Put, $"api/v2/clients/orders/{Uri.EscapeDataString(shipment.ProviderReference!)}", handling, cancellationToken);
            }
            catch (WakilniException exception)
            {
                bookings[index] = bookings[index] with { Warning = $"Booked, but Wakilni did not take the delivery options: {exception.Message}" };
            }
        }

        foreach (WakilniBooking booking in bookings)
            if (booking.Shipment is Shipment shipment)
                _shipments[shipment.Id] = shipment;

        return bookings;
    }

    /// <summary>
    /// Sends the current recipient, address, collection, packages and delivery options of a booked delivery to Wakilni.
    /// Wakilni only accepts changes while the delivery is still pending.
    /// </summary>
    public async Task UpdateAsync(string providerReference, ShipmentRequest request, CancellationToken cancellationToken = default)
    {
        Dictionary<string, object?> body = DeliveryBody(request);

        foreach (string key in BookingOnlyFields)
            body.Remove(key);

        body["new_receiver_location"] = true;

        foreach ((string key, object? value) in HandlingOptions(request, includeUnset: true))
            body[key] = value;

        await SendAsync(HttpMethod.Put, $"api/v2/clients/orders/{Uri.EscapeDataString(providerReference)}", body, cancellationToken);
    }

    /// <summary>Every status a Wakilni order went through, oldest first, with the driver's comments.</summary>
    public async Task<WakilniHistory> GetHistoryAsync(string providerReference, CancellationToken cancellationToken = default)
    {
        JsonElement root = await SendAsync(HttpMethod.Get, $"api/v2/clients/orders/status?order_id={Uri.EscapeDataString(providerReference)}", null, cancellationToken);
        JsonElement data = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out JsonElement inner) && inner.ValueKind == JsonValueKind.Object ? inner : root;

        // Wakilni lists the newest step first and times steps to the second, so steps within the same second keep that order reversed.
        List<WakilniStatusEntry> entries = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("logs", out JsonElement logs) && logs.ValueKind == JsonValueKind.Array
            ? [.. logs.EnumerateArray()
                .Select((log, index) => (Index: index, Entry: new WakilniStatusEntry(Number(log, "status_code") ?? WakilniStatuses.Pending, Text(log, "status") ?? WakilniStatuses.Label(Number(log, "status_code") ?? WakilniStatuses.Pending), Date(log, "created_at"))))
                .OrderBy(step => step.Entry.At ?? DateTimeOffset.MinValue)
                .ThenByDescending(step => step.Index)
                .Select(step => step.Entry)]
            : [];

        List<string> comments = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("comments", out JsonElement notes) && notes.ValueKind == JsonValueKind.Array
            ? [.. notes.EnumerateArray().Select(comment => comment.ValueKind == JsonValueKind.String ? comment.GetString() : comment.ToString()).OfType<string>().Where(comment => comment.Length > 0)]
            : [];

        return new WakilniHistory(Text(data, "tracking_id"), entries, comments);
    }

    public Task<Shipment?> GetShipmentAsync(Guid shipmentId, CancellationToken cancellationToken = default)
    {
        _shipments.TryGetValue(shipmentId, out Shipment? shipment);
        return Task.FromResult(shipment);
    }

    public async Task<Shipment> RefreshTrackingAsync(Guid shipmentId, CancellationToken cancellationToken = default)
    {
        Shipment current = await GetShipmentAsync(shipmentId, cancellationToken) ?? throw new KeyNotFoundException($"Shipment '{shipmentId}' was not found.");
        Shipment tracked = await TrackAsync(current.ProviderReference ?? throw new InvalidOperationException("The Wakilni shipment has no order id."), cancellationToken);
        Shipment updated = current with { Status = tracked.Status, DeliveredAt = tracked.DeliveredAt ?? current.DeliveredAt, Metadata = tracked.Metadata };
        _shipments[shipmentId] = updated;
        return updated;
    }

    /// <summary>Reads the status history of a Wakilni order by its order id and maps the latest step.</summary>
    public async Task<Shipment> TrackAsync(string providerReference, CancellationToken cancellationToken = default) =>
        ToShipment(providerReference, await GetHistoryAsync(providerReference, cancellationToken));

    /// <summary>The shipment a status history describes: its latest step decides the status.</summary>
    public Shipment ToShipment(string providerReference, WakilniHistory history)
    {
        WakilniStatusEntry? latest = history.Latest;
        int code = latest?.Code ?? WakilniStatuses.Pending;
        Dictionary<string, string> metadata = new()
        {
            [WakilniMetadata.StatusCode] = code.ToString(CultureInfo.InvariantCulture),
            [WakilniMetadata.Status] = latest?.Status ?? WakilniStatuses.Label(code)
        };

        if (history.Comments.Count > 0)
            metadata[WakilniMetadata.Comments] = string.Join("\n", history.Comments);

        ShipmentStatuses status = WakilniStatuses.Map(code, latest?.Status);

        return new Shipment(StableId(providerReference), Name, WakilniServices.Bulk, status, history.TrackingId,
            DeliveredAt: status == ShipmentStatuses.Delivered ? latest?.At ?? DateTimeOffset.UtcNow : null,
            ProviderReference: providerReference,
            Metadata: metadata);
    }

    public async Task CancelShipmentAsync(Guid shipmentId, CancellationToken cancellationToken = default)
    {
        Shipment current = await GetShipmentAsync(shipmentId, cancellationToken) ?? throw new KeyNotFoundException($"Shipment '{shipmentId}' was not found.");
        await CancelAsync(current.ProviderReference ?? throw new InvalidOperationException("The Wakilni shipment has no order id."), null, cancellationToken);
        _shipments[shipmentId] = current with { Status = ShipmentStatuses.Cancelled };
    }

    /// <summary>Cancels a Wakilni order. Wakilni only allows it while the order is still pending.</summary>
    public async Task CancelAsync(string providerReference, string? reason = null, CancellationToken cancellationToken = default) =>
        await SendAsync(HttpMethod.Put, $"api/v2/clients/orders/{Uri.EscapeDataString(providerReference)}/cancel", new Dictionary<string, object?> { ["reason"] = reason ?? "Cancelled by the shop" }, cancellationToken);

    public Task<IReadOnlyList<PickupLocation>> GetPickupLocationsAsync(string countryCode, string? subdivisionCode = null, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PickupLocation>>([]);

    /// <summary>The delivery areas Wakilni knows, optionally filtered by name. Sending one of these names as the area helps Wakilni price and route the delivery.</summary>
    public async Task<IReadOnlyList<WakilniArea>> GetAreasAsync(string? search = null, CancellationToken cancellationToken = default)
    {
        List<WakilniArea> areas = [];
        string filter = string.IsNullOrWhiteSpace(search) ? string.Empty : $"&search=name:{Uri.EscapeDataString(search)}&searchJoin=and&with_filter=true";

        for (int page = 1; page <= 50; page++)
        {
            JsonElement root = await SendAsync(HttpMethod.Get, $"api/v2/areas?with_pagination=true&page={page}{filter}", null, cancellationToken);
            List<WakilniArea> found = [.. AreaItems(root).Select(ToArea).OfType<WakilniArea>()];
            areas.AddRange(found);

            if (found.Count == 0 || !HasNextPage(root, page))
                break;
        }

        return [.. areas.DistinctBy(area => area.Id)];
    }

    private Dictionary<string, object?> DeliveryBody(ShipmentRequest request)
    {
        LogisticsAddress destination = request.Destination;
        Dictionary<string, string> metadata = request.Metadata ?? [];
        (string firstName, string lastName) = SplitName(destination.RecipientName);
        string? directions = string.Join(", ", new[] { destination.Line1, destination.Line2 }.Where(line => !string.IsNullOrWhiteSpace(line)));

        Dictionary<string, object?> body = new()
        {
            ["get_order_details"] = false,
            ["get_barcode"] = options.RequestLabels,
            ["waybill"] = request.Reference,
            ["receiver_id"] = metadata.TryGetValue(WakilniMetadata.ReceiverId, out string? receiverId) ? receiverId : StableNumber(destination.PhoneNumber ?? destination.RecipientName ?? directions),
            ["receiver_first_name"] = firstName,
            ["receiver_last_name"] = lastName,
            ["receiver_phone_number"] = destination.PhoneNumber,
            ["receiver_email"] = destination.Email,
            ["receiver_location_id"] = metadata.TryGetValue(WakilniMetadata.ReceiverLocationId, out string? locationId) ? locationId : StableNumber($"{destination.PhoneNumber}|{destination.Locality}|{directions}"),
            ["receiver_longitude"] = destination.Longitude,
            ["receiver_latitude"] = destination.Latitude,
            ["receiver_building"] = metadata.GetValueOrDefault(WakilniMetadata.Building),
            ["receiver_floor"] = metadata.TryGetValue(WakilniMetadata.Floor, out string? floor) && int.TryParse(floor, out int floorNumber) ? floorNumber : null,
            ["receiver_directions"] = string.IsNullOrWhiteSpace(directions) ? null : directions,
            ["receiver_area"] = destination.Locality,
            ["note"] = request.Notes,
            ["car_needed"] = Flag(metadata, WakilniMetadata.CarNeeded),
            ["is_express"] = string.Equals(request.ServiceCode, WakilniServices.Express, StringComparison.OrdinalIgnoreCase),
            ["packages"] = Packages(request)
        };

        if (request.CashOnDelivery is Money collect && collect.Value > 0)
        {
            body["currency"] = Currency(collect.Currency);
            body["cash_collection_type_id"] = CollectOnDelivery;
            body["collection_amount"] = collect.Value;
        }
        else
        {
            body["currency"] = UsDollars;
            body["cash_collection_type_id"] = AlreadyPaid;
            body["collection_amount"] = 0m;
        }

        return body;
    }

    private object[] Packages(ShipmentRequest request)
    {
        int typeId = request.Metadata?.GetValueOrDefault(WakilniMetadata.PackageType) is string type && int.TryParse(type, out int value) ? value : options.PackageTypeId;

        return request.Items.Count > 0
            ? [.. request.Items.Select(item => new Dictionary<string, object?> { ["quantity"] = Math.Max(1, item.Quantity), ["type_id"] = typeId, ["name"] = item.Name, ["sku"] = item.Sku })]
            : [new Dictionary<string, object?> { ["quantity"] = Math.Max(1, request.Packages.Count), ["type_id"] = typeId }];
    }

    /// <summary>The delivery options only the update endpoint takes: those switched on, or all of them when <paramref name="includeUnset"/> is set.</summary>
    private static Dictionary<string, object?> HandlingOptions(ShipmentRequest request, bool includeUnset = false)
    {
        Dictionary<string, string> metadata = request.Metadata ?? [];
        Dictionary<string, object?> handling = [];

        foreach ((string field, string key) in HandlingFlags)
            if (includeUnset || Flag(metadata, key))
                handling[field] = Flag(metadata, key);

        return handling;
    }

    private static bool Flag(Dictionary<string, string> metadata, string key) =>
        metadata.TryGetValue(key, out string? value) && bool.TryParse(value, out bool flag) && flag;

    private Shipment ToShipment(ShipmentRequest request, JsonElement delivery)
    {
        string orderId = Text(delivery, "delivery_id") ?? Text(delivery, "id") ?? throw new InvalidOperationException("Wakilni did not return a delivery id.");
        string? trackingId = Text(delivery, "tracking_id");
        Uri? trackingUrl = Uri.TryCreate(Text(delivery, "tracking_url"), UriKind.Absolute, out Uri? url) ? url : null;
        List<ShipmentDocument> documents = [];

        if (Text(delivery, "barcode_label") is { Length: > 0 } label)
            documents.Add(new ShipmentDocument("label", "image/png", label));

        Dictionary<string, string> metadata = new()
        {
            [WakilniMetadata.StatusCode] = "1",
            [WakilniMetadata.Status] = WakilniStatuses.Label(1)
        };

        return new Shipment(StableId(orderId), Name, request.ServiceCode, ShipmentStatuses.Pending, trackingId, trackingUrl,
            ProviderReference: orderId, Documents: documents, Metadata: metadata);
    }

    private async Task<JsonElement> SendAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        options.Validate();

        for (int attempt = 0; ; attempt++)
        {
            string token = await TokenAsync(attempt > 0, cancellationToken);
            using HttpRequestMessage message = new(method, path);
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            if (body is not null)
                message.Content = JsonContent.Create(body);

            using HttpResponseMessage response = await httpClient.SendAsync(message, cancellationToken);
            string json = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
                continue;

            if (!response.IsSuccessStatusCode)
                throw new WakilniException(response.StatusCode, Problem(json) ?? $"Wakilni answered {(int)response.StatusCode} {response.ReasonPhrase}.");

            if (string.IsNullOrWhiteSpace(json))
                return default;

            using JsonDocument document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
    }

    private async Task<string> TokenAsync(bool renew, CancellationToken cancellationToken)
    {
        if (!renew && _token is string cached)
            return cached;

        await _tokenLock.WaitAsync(cancellationToken);

        try
        {
            if (!renew && _token is string current)
                return current;

            using HttpRequestMessage message = new(HttpMethod.Get, "api/v2/third_party/auth_token")
            {
                Content = JsonContent.Create(new Dictionary<string, string> { ["key"] = options.Key, ["secret"] = options.Secret })
            };
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using HttpResponseMessage response = await httpClient.SendAsync(message, cancellationToken);
            string json = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
                throw new WakilniException(response.StatusCode, Problem(json) ?? "Wakilni refused the key and secret.");

            using JsonDocument document = JsonDocument.Parse(json);
            _token = Text(document.RootElement, "token") ?? throw new WakilniException(response.StatusCode, "Wakilni did not return a token.");
            return _token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static string? Problem(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            List<string> parts = [];

            if (Text(root, "message") is { Length: > 0 } message)
                parts.Add(message);

            if (root.TryGetProperty("errors", out JsonElement errors) && errors.ValueKind == JsonValueKind.Object)
                foreach (JsonProperty field in errors.EnumerateObject())
                    parts.Add(field.Value.ValueKind == JsonValueKind.Array
                        ? $"{field.Name}: {string.Join(" ", field.Value.EnumerateArray().Select(value => value.ToString()))}"
                        : $"{field.Name}: {field.Value}");

            return parts.Count > 0 ? string.Join(" ", parts) : null;
        }
        catch (JsonException)
        {
            return json.Length > 300 ? json[..300] : json;
        }
    }

    private static IEnumerable<JsonElement> AreaItems(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
            return root.EnumerateArray();

        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("data", out JsonElement data))
            return [];

        if (data.ValueKind == JsonValueKind.Array)
            return data.EnumerateArray();

        return data.ValueKind == JsonValueKind.Object && data.TryGetProperty("data", out JsonElement page) && page.ValueKind == JsonValueKind.Array ? page.EnumerateArray() : [];
    }

    private static WakilniArea? ToArea(JsonElement item)
    {
        string? name = Text(item, "name") ?? Text(item, "label") ?? Text(item, "area");
        return name is null ? null : new WakilniArea(Text(item, "id") ?? name, name, Text(item, "zone") ?? Text(item, "zone_name"));
    }

    private static bool HasNextPage(JsonElement root, int page)
    {
        JsonElement container = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out JsonElement data) && data.ValueKind == JsonValueKind.Object ? data : root;

        if (container.ValueKind != JsonValueKind.Object)
            return false;

        if (container.TryGetProperty("next_page_url", out JsonElement next))
            return next.ValueKind == JsonValueKind.String;

        if (Number(container, "last_page") is int lastPage)
            return page < lastPage;

        JsonElement meta = container.TryGetProperty("meta", out JsonElement value) ? value : default;
        return meta.ValueKind == JsonValueKind.Object && Number(meta, "last_page") is int metaLast && page < metaLast;
    }

    private static string? Text(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out JsonElement value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static int? Number(JsonElement element, string property) =>
        int.TryParse(Text(element, property), NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) ? number : null;

    private static DateTimeOffset? Date(JsonElement element, string property) =>
        DateTimeOffset.TryParse(Text(element, property), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset date) ? date : null;

    private static int Currency(string currency) => string.Equals(currency, "LBP", StringComparison.OrdinalIgnoreCase) ? LebanesePounds : UsDollars;

    private static (string First, string Last) SplitName(string? name)
    {
        string[] parts = (name ?? string.Empty).Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            0 => ("Customer", "-"),
            1 => (parts[0], "-"),
            _ => (parts[0], parts[1])
        };
    }

    /// <summary>Wakilni wants numeric ids for the receiver and their location from the sender's system; a stable hash keeps the same person on the same id.</summary>
    private static long StableNumber(string? value)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty));
        return (BitConverter.ToInt64(hash, 0) & long.MaxValue) % 2_000_000_000L + 1;
    }

    private static Guid StableId(string providerReference) =>
        new(MD5.HashData(Encoding.UTF8.GetBytes($"wakilni:{providerReference}")));
}

/// <summary>The outcome of one delivery in a bulk booking: the shipment, or why Wakilni rejected it, and anything Wakilni booked only in part.</summary>
public sealed record WakilniBooking(ShipmentRequest Request, Shipment? Shipment, string? Error, string? Warning = null);

/// <summary>One step of a Wakilni order, as Wakilni names it, for example "Success, Rejected".</summary>
public sealed record WakilniStatusEntry(int Code, string Status, DateTimeOffset? At);

public sealed record WakilniHistory(string? TrackingId, IReadOnlyList<WakilniStatusEntry> Entries, IReadOnlyList<string> Comments)
{
    public WakilniStatusEntry? Latest => Entries.Count > 0 ? Entries[^1] : null;
}

public sealed record WakilniArea(string Id, string Name, string? Zone = null);

public sealed class WakilniException(HttpStatusCode statusCode, string message) : InvalidOperationException(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}

public static class WakilniStatuses
{
    public const int Pending = 1;
    public const int Confirmed = 2;
    public const int Processing = 3;
    public const int Success = 4;
    public const int Failed = 5;
    public const int Declined = 6;
    public const int Canceled = 7;
    public const int ClosedFailed = 8;
    public const int Postponed = 9;
    public const int PendingCancellation = 10;

    /// <summary>Statuses after which Wakilni no longer moves the order.</summary>
    public static bool IsFinal(int code) => code is Success or Declined or Canceled or ClosedFailed;

    /// <summary>Wakilni closes a delivery the recipient refused with the success code and a status such as "Success, Rejected".</summary>
    public static bool IsRejected(int code, string? status) =>
        code == Success && status?.Contains("reject", StringComparison.OrdinalIgnoreCase) == true;

    public static ShipmentStatuses Map(int code, string? status = null) => code switch
    {
        Success when IsRejected(code, status) => ShipmentStatuses.Returned,
        Confirmed => ShipmentStatuses.LabelCreated,
        Processing => ShipmentStatuses.OutForDelivery,
        Success => ShipmentStatuses.Delivered,
        Failed or Postponed => ShipmentStatuses.DeliveryFailed,
        Declined or Canceled => ShipmentStatuses.Cancelled,
        ClosedFailed => ShipmentStatuses.Returned,
        _ => ShipmentStatuses.Pending
    };

    public static string Label(int code) => code switch
    {
        Pending => "Pending",
        Confirmed => "Confirmed",
        Processing => "Processing",
        Success => "Success",
        Failed => "Failed",
        Declined => "Declined",
        Canceled => "Canceled",
        ClosedFailed => "Closed failed",
        Postponed => "Postponed",
        PendingCancellation => "Pending cancellation",
        _ => $"Status {code}"
    };
}
