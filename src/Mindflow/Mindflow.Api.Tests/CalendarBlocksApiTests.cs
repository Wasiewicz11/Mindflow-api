using System.Net.Http.Json;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Tests.Infrastructure;

namespace Mindflow.Api.Tests;

public class CalendarBlocksApiTests(MindflowApiFactory factory) : IClassFixture<MindflowApiFactory>
{
    [LocalDatabaseFact]
    public async Task Range_includes_all_day_events_that_started_before_it()
    {
        var user = await factory.CreateUserAsync();
        var trip = await SeedAsync(user.Id, Utc(2026, 11, 1), 3 * 24 * 60, allDay: true);
        await SeedAsync(user.Id, Utc(2026, 10, 30), 24 * 60, allDay: true);
        await SeedAsync(user.Id, Utc(2026, 11, 1, 10), 600, allDay: false);
        var inside = await SeedAsync(user.Id, Utc(2026, 11, 2, 9), 60, allDay: false);
        using var client = factory.CreateUserClient(user.Id);

        var blocks = await client.GetFromJsonAsync<List<CalendarBlockResponse>>(
            "/calendar/blocks?from=2026-11-02&to=2026-11-02", MindflowApiFactory.Json);

        Assert.Equal([trip, inside], blocks!.Select(b => b.Id).ToList());
        Assert.True(blocks![0].IsAllDay);
        Assert.False(blocks[1].IsAllDay);
    }

    private static DateTimeOffset Utc(int year, int month, int day, int hour = 0) =>
        new(year, month, day, hour, 0, 0, TimeSpan.Zero);

    private async Task<Guid> SeedAsync(Guid userId, DateTimeOffset start, int minutes, bool allDay)
    {
        var block = new CalendarBlock
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = "Wydarzenie",
            StartAt = start,
            DurationMinutes = minutes,
            IsAllDay = allDay,
            Provider = CalendarBlockProvider.Google,
            ExternalEventId = Guid.NewGuid().ToString("N"),
            SyncStatus = CalendarBlockSyncStatus.Synced,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await factory.WithDbAsync(async db =>
        {
            db.CalendarBlocks.Add(block);
            await db.SaveChangesAsync();
        });
        return block.Id;
    }
}
