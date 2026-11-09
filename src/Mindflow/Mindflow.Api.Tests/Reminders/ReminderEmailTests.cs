using Mindflow.Api.Services.Reminders;

namespace Mindflow.Api.Tests.Reminders;

public class ReminderEmailTests
{
    private static readonly ReminderMessage Message = new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "⏰ Telefon <do> księgowej", "Za 10 min · termin dziś 14:00", "dziś 14:00",
        DateTimeOffset.UtcNow, "https://app.test/zadania/1", "tok");

    [Fact]
    public void Subject_has_title_and_time()
    {
        var email = ReminderEmail.Build(Message, "https://app.test/przypomnienie?t=tok", "a@b.pl");
        Assert.Equal("⏰ Telefon <do> księgowej o 14:00", email.Subject);
        Assert.Equal("a@b.pl", email.To);
    }

    [Fact]
    public void Html_escapes_title_and_links_to_actions()
    {
        var email = ReminderEmail.Build(Message, "https://app.test/przypomnienie?t=tok", "a@b.pl");
        Assert.Contains("Telefon &lt;do&gt; księgowej", email.Html);
        Assert.DoesNotContain("<do>", email.Html);
        Assert.Contains("https://app.test/przypomnienie?t=tok&amp;a=done", email.Html);
        Assert.Contains("https://app.test/przypomnienie?t=tok&amp;a=snooze", email.Html);
        Assert.Contains("https://app.test/zadania/1", email.Html);
        Assert.Contains("Za 10 min · termin dziś 14:00", email.Text);
        Assert.Contains("https://app.test/przypomnienie?t=tok&a=done", email.Text);
    }

    [Fact]
    public void Subject_collapses_line_breaks_in_title()
    {
        var message = Message with { Title = "⏰ Linia\r\nBcc: x@y.pl" };
        var email = ReminderEmail.Build(message, "https://app.test/przypomnienie?t=tok", "a@b.pl");
        Assert.DoesNotContain('\r', email.Subject);
        Assert.DoesNotContain('\n', email.Subject);
        Assert.Equal("⏰ Linia Bcc: x@y.pl o 14:00", email.Subject);
    }
}
