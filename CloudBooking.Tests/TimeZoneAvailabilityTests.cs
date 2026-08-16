using AngryMonkey.CloudBooking;

namespace CloudBooking.Tests;

public class TimeZoneAvailabilityTests
{
    [Fact]
    public async Task IsAvailableAsync_converts_request_to_resource_time_zone()
    {
        InMemoryBookingStore store = new();
        Guid resourceId = Guid.NewGuid();
        await store.SaveResourceAsync(new(resourceId, "New York resource", TimeZoneId: "America/New_York"));
        await store.SaveAvailabilityRulesAsync(resourceId, [new(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(17, 0))]);
        AvailabilityService availability = new(store);
        DateTimeOffset startsAt = new(2026, 8, 17, 13, 0, 0, TimeSpan.Zero);

        bool result = await availability.IsAvailableAsync(resourceId, startsAt, startsAt.AddHours(1), 1);

        Assert.True(result);
    }
}
