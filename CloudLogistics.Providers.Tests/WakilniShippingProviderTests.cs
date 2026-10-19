using System.Net;
using System.Text.Json;
using AngryMonkey.Cloud.Geography;
using AngryMonkey.CloudLogistics;
using AngryMonkey.CloudLogistics.Wakilni;

namespace CloudLogistics.Providers.Tests;

public class WakilniShippingProviderTests
{
    private static WakilniOptions Options => new() { Key = "key", Secret = "secret", PickupLocationId = 7 };

    private static ShipmentRequest Request(Money? collect = null) => new(TestAddresses.Origin, TestAddresses.Destination with { Locality = "Hamra", Email = "receiver@example.com" }, TestAddresses.Packages, WakilniServices.Bulk, "AM-261019-7K3Q")
    {
        CashOnDelivery = collect,
        Items = [new ShipmentItem("Day Cream", 2, "day-cream")],
        Notes = "Call before arriving"
    };

    [Fact]
    public async Task CreateShipmentsAsync_books_every_delivery_in_one_bulk()
    {
        List<(string Path, string? Body)> calls = [];
        StubHttpMessageHandler handler = new((request, _) =>
        {
            string path = request.RequestUri!.PathAndQuery;
            calls.Add((path, request.Content?.ReadAsStringAsync().Result));

            return path switch
            {
                "/api/v2/third_party/auth_token" => StubHttpMessageHandler.Json("""{"message":"ok","token":"abc"}"""),
                "/api/v2/clients/start_bulk" => StubHttpMessageHandler.Json("""{"message":"ok","bulk_id":55}"""),
                "/api/v2/clients/end_bulk/55" => StubHttpMessageHandler.Json("""{"message":"ok","bulk_id":55}"""),
                _ => StubHttpMessageHandler.Json($$"""{"message":"ok","delivery_id":{{calls.Count * 100}},"tracking_id":"TRK{{calls.Count}}","tracking_url":"https://track.wakilni.com/TRK{{calls.Count}}","packages":[]}""")
            };
        });
        WakilniShippingProvider provider = new(new HttpClient(handler) { BaseAddress = new("https://wakilni.test/") }, Options);

        IReadOnlyList<WakilniBooking> bookings = await provider.CreateShipmentsAsync([Request(new Money("USD", 45m)), Request()]);

        Assert.All(bookings, booking => Assert.NotNull(booking.Shipment));
        Assert.Single(calls, call => call.Path == "/api/v2/clients/start_bulk");
        Assert.Equal(2, calls.Count(call => call.Path == "/api/v2/clients/add_delivery/55"));
        Assert.Equal("/api/v2/clients/end_bulk/55", calls[^1].Path);
        Assert.Equal("TRK3", bookings[0].Shipment!.TrackingNumber);
        Assert.Equal("300", bookings[0].Shipment!.ProviderReference);

        using JsonDocument cash = JsonDocument.Parse(calls.First(call => call.Path.StartsWith("/api/v2/clients/add_delivery")).Body!);
        Assert.Equal(52, cash.RootElement.GetProperty("cash_collection_type_id").GetInt32());
        Assert.Equal(45m, cash.RootElement.GetProperty("collection_amount").GetDecimal());
        Assert.Equal("Hamra", cash.RootElement.GetProperty("receiver_area").GetString());
        Assert.Equal("Day Cream", cash.RootElement.GetProperty("packages")[0].GetProperty("name").GetString());
        Assert.Equal(2, cash.RootElement.GetProperty("packages")[0].GetProperty("quantity").GetInt32());

        using JsonDocument paid = JsonDocument.Parse(calls.Last(call => call.Path.StartsWith("/api/v2/clients/add_delivery")).Body!);
        Assert.Equal(54, paid.RootElement.GetProperty("cash_collection_type_id").GetInt32());
    }

    [Fact]
    public async Task CreateShipmentsAsync_reports_a_rejected_delivery_and_still_ends_the_bulk()
    {
        int deliveries = 0;
        bool ended = false;
        StubHttpMessageHandler handler = new((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/api/v2/third_party/auth_token" => StubHttpMessageHandler.Json("""{"token":"abc"}"""),
            "/api/v2/clients/start_bulk" => StubHttpMessageHandler.Json("""{"bulk_id":"9"}"""),
            "/api/v2/clients/end_bulk/9" => Ended(),
            _ => ++deliveries == 1
                ? StubHttpMessageHandler.Json("""{"message":"The given data was invalid.","errors":{"receiver_phone_number":["The receiver phone number is required."]}}""", HttpStatusCode.UnprocessableEntity)
                : StubHttpMessageHandler.Json("""{"delivery_id":12,"tracking_id":"T12"}""")
        });
        WakilniShippingProvider provider = new(new HttpClient(handler) { BaseAddress = new("https://wakilni.test/") }, Options);

        IReadOnlyList<WakilniBooking> bookings = await provider.CreateShipmentsAsync([Request(), Request()]);

        Assert.Null(bookings[0].Shipment);
        Assert.Contains("receiver_phone_number", bookings[0].Error);
        Assert.Equal("12", bookings[1].Shipment!.ProviderReference);
        Assert.True(ended);

        HttpResponseMessage Ended()
        {
            ended = true;
            return StubHttpMessageHandler.Json("""{"bulk_id":"9"}""");
        }
    }

    [Fact]
    public async Task TrackAsync_maps_the_latest_status_log()
    {
        StubHttpMessageHandler handler = new((request, _) => request.RequestUri!.AbsolutePath == "/api/v2/third_party/auth_token"
            ? StubHttpMessageHandler.Json("""{"token":"abc"}""")
            : StubHttpMessageHandler.Json("""{"success":true,"data":{"tracking_id":"T12","comments":["Left at the door"],"logs":[{"status":"Success","status_code":4,"created_at":"2026-10-19 14:00:00"},{"status":"Pending","status_code":1,"created_at":"2026-10-18 09:00:00"}]}}"""));
        WakilniShippingProvider provider = new(new HttpClient(handler) { BaseAddress = new("https://wakilni.test/") }, Options);

        Shipment shipment = await provider.TrackAsync("12");

        Assert.Equal(ShipmentStatuses.Delivered, shipment.Status);
        Assert.Equal("4", shipment.Metadata![WakilniMetadata.StatusCode]);
        Assert.Equal("Left at the door", shipment.Metadata[WakilniMetadata.Comments]);
        Assert.NotNull(shipment.DeliveredAt);
        Assert.Contains(handler.Requests, call => call.Uri!.Query == "?order_id=12");
    }

    [Fact]
    public async Task Requests_renew_the_token_once_when_it_expires()
    {
        int tokens = 0;
        int statusCalls = 0;
        StubHttpMessageHandler handler = new((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/v2/third_party/auth_token")
                return StubHttpMessageHandler.Json($$"""{"token":"t{{++tokens}}"}""");

            return ++statusCalls == 1
                ? StubHttpMessageHandler.Json("""{"message":"Unauthenticated."}""", HttpStatusCode.Unauthorized)
                : StubHttpMessageHandler.Json("""{"data":{"logs":[{"status_code":3}]}}""");
        });
        WakilniShippingProvider provider = new(new HttpClient(handler) { BaseAddress = new("https://wakilni.test/") }, Options);

        Shipment shipment = await provider.TrackAsync("5");

        Assert.Equal(ShipmentStatuses.OutForDelivery, shipment.Status);
        Assert.Equal(2, tokens);
    }

    [Fact]
    public async Task GetAreasAsync_reads_paginated_areas()
    {
        StubHttpMessageHandler handler = new((request, _) => request.RequestUri!.AbsolutePath == "/api/v2/third_party/auth_token"
            ? StubHttpMessageHandler.Json("""{"token":"abc"}""")
            : request.RequestUri.Query.Contains("page=1")
                ? StubHttpMessageHandler.Json("""{"data":{"data":[{"id":1,"name":"Hamra"},{"id":2,"name":"Achrafieh"}],"last_page":2}}""")
                : StubHttpMessageHandler.Json("""{"data":{"data":[{"id":3,"name":"Jounieh"}],"last_page":2}}"""));
        WakilniShippingProvider provider = new(new HttpClient(handler) { BaseAddress = new("https://wakilni.test/") }, Options);

        IReadOnlyList<WakilniArea> areas = await provider.GetAreasAsync();

        Assert.Equal(["Hamra", "Achrafieh", "Jounieh"], areas.Select(area => area.Name));
    }

    [Fact]
    public void Final_statuses_stop_tracking()
    {
        Assert.True(WakilniStatuses.IsFinal(WakilniStatuses.Success));
        Assert.True(WakilniStatuses.IsFinal(WakilniStatuses.Canceled));
        Assert.False(WakilniStatuses.IsFinal(WakilniStatuses.Postponed));
        Assert.Equal(ShipmentStatuses.Returned, WakilniStatuses.Map(WakilniStatuses.ClosedFailed));
    }
}
