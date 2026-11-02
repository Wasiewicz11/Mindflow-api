using Google.Apis.Calendar.v3.Data;
using Mindflow.Api.Services.GoogleCalendar;

namespace Mindflow.Api.Tests;

public class GoogleEventMappingTests
{
    [Fact]
    public void All_day_event_becomes_an_all_day_block_starting_at_utc_midnight()
    {
        var change = GoogleCalendarClient.MapChange(AllDay("2026-11-02", "2026-11-03"));

        Assert.NotNull(change);
        Assert.True(change.IsAllDay);
        Assert.Equal(new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero), change.Start);
        Assert.Equal(24 * 60, change.DurationMinutes);
        Assert.Equal("Urodziny", change.Title);
    }

    [Fact]
    public void Multi_day_event_lasts_until_its_exclusive_end_date()
    {
        var change = GoogleCalendarClient.MapChange(AllDay("2026-11-02", "2026-11-05"));

        Assert.Equal(3 * 24 * 60, change!.DurationMinutes);
    }

    [Fact]
    public void All_day_event_without_end_lasts_one_day()
    {
        var change = GoogleCalendarClient.MapChange(AllDay("2026-11-02", null));

        Assert.Equal(24 * 60, change!.DurationMinutes);
    }

    [Fact]
    public void Timed_event_is_not_all_day()
    {
        var start = new DateTimeOffset(2026, 11, 2, 9, 30, 0, TimeSpan.FromHours(1));
        var change = GoogleCalendarClient.MapChange(new Event
        {
            Id = "timed",
            Summary = "Spotkanie",
            Start = new EventDateTime { DateTimeDateTimeOffset = start },
            End = new EventDateTime { DateTimeDateTimeOffset = start.AddMinutes(45) }
        });

        Assert.False(change!.IsAllDay);
        Assert.Equal(start, change.Start);
        Assert.Equal(45, change.DurationMinutes);
    }

    private static Event AllDay(string start, string? end) => new()
    {
        Id = "all-day",
        Summary = "Urodziny",
        Start = new EventDateTime { Date = start },
        End = end is null ? null : new EventDateTime { Date = end }
    };
}
