using Mindflow.Api.Services.Reminders;

namespace Mindflow.Api.Tests.Reminders;

public class ReminderTextTests
{
    private static readonly TimeZoneInfo Warsaw = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");

    private static DateTimeOffset At(string value) =>
        new(DateTime.Parse(value), Warsaw.GetUtcOffset(DateTime.Parse(value)));

    [Theory]
    [InlineData(0, "Teraz", "dziś 14:00")]
    [InlineData(10, "Za 10 min", "dziś 14:00")]
    [InlineData(90, "Za 1 h 30 min", "dziś 14:00")]
    [InlineData(120, "Za 2 h", "dziś 14:00")]
    [InlineData(1440, "Za 1 dzień", "jutro 14:00")]
    public void Body_says_how_long_until_due(int minutes, string lead, string localDue)
    {
        var due = new DueReminder(At("2030-01-10 14:00"), At("2030-01-10 14:00").AddMinutes(-minutes), minutes, false, "k");
        var text = ReminderText.Build("Telefon", due, Warsaw, At("2030-01-10 14:00").AddMinutes(-minutes));

        Assert.Equal("⏰ Telefon", text.Title);
        Assert.StartsWith(lead, text.Body);
        Assert.Contains(localDue, text.Body);
        Assert.Equal(localDue, text.LocalDue);
    }

    [Fact]
    public void Tomorrow_and_other_days_are_named()
    {
        var due = new DueReminder(At("2030-01-11 09:00"), At("2030-01-10 09:00"), 1440, false, "k");
        Assert.Equal("jutro 09:00", ReminderText.Build("X", due, Warsaw, At("2030-01-10 09:00")).LocalDue);
        var later = new DueReminder(At("2030-01-14 09:00"), At("2030-01-10 09:00"), 5760, false, "k");
        Assert.Equal("14.01 09:00", ReminderText.Build("X", later, Warsaw, At("2030-01-10 09:00")).LocalDue);
    }

    [Fact]
    public void Truncation_never_leaves_a_lone_surrogate()
    {
        var due = new DueReminder(At("2030-01-10 14:00"), At("2030-01-10 14:00"), 0, false, "k");
        var text = ReminderText.Build(new string('a', 78) + "😀😀", due, Warsaw, At("2030-01-10 14:00"));

        Assert.DoesNotContain(text.Title.Where(char.IsSurrogate), _ => true);
        Assert.EndsWith("…", text.Title);
    }

    [Fact]
    public void Snoozed_reminder_says_so_and_long_titles_are_cut()
    {
        var due = new DueReminder(At("2030-01-10 14:00"), At("2030-01-10 14:10"), 10, true, "k");
        var text = ReminderText.Build(new string('a', 200), due, Warsaw, At("2030-01-10 14:10"));
        Assert.StartsWith("Odłożone przypomnienie", text.Body);
        Assert.True(text.Title.Length <= 82);
    }
}
