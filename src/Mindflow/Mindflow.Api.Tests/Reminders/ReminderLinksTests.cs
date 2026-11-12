using Microsoft.Extensions.Configuration;
using Mindflow.Api.Services.Reminders;

namespace Mindflow.Api.Tests.Reminders;

public class ReminderLinksTests
{
    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value)).Build();

    [Fact]
    public void Uses_notification_urls_and_falls_back_to_cors()
    {
        var taskId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var explicitConfig = Config(("Notifications:FrontendUrl", "https://app.test/"), ("Notifications:PublicApiUrl", "https://api.test/"));
        var fallback = Config(("Cors:FrontendUrl", "http://localhost:4200"));

        Assert.Equal("https://app.test/zadania/22222222-2222-2222-2222-222222222222", ReminderLinks.TaskUrl(explicitConfig, taskId));
        Assert.Equal("https://api.test/notifications/reminder-actions", ReminderLinks.ActionApiUrl(explicitConfig));
        Assert.Equal("http://localhost:4200/przypomnienie?t=a%2Bb", ReminderLinks.ActionPageUrl(fallback, "a+b"));
        Assert.Equal("/notifications/reminder-actions", ReminderLinks.ActionApiUrl(fallback));
    }
}
