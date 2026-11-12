using System.Net.Http.Json;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Tests.Infrastructure;

namespace Mindflow.Api.Tests.Reminders;

public class NotificationSettingsTelegramApiTests(MindflowApiFactory factory) : IClassFixture<MindflowApiFactory>
{
    [LocalDatabaseFact]
    public async Task Telegram_reminders_default_on_and_old_clients_keep_the_value()
    {
        var user = await factory.CreateUserAsync();
        var client = factory.CreateUserClient(user.Id);
        var initial = await client.GetFromJsonAsync<NotificationSettingsResponse>("/notifications/settings", MindflowApiFactory.Json);
        Assert.True(initial!.TelegramRemindersEnabled);

        var off = await client.PutAsJsonAsync("/notifications/settings", Body(initial, false));
        Assert.False((await off.Content.ReadFromJsonAsync<NotificationSettingsResponse>(MindflowApiFactory.Json))!.TelegramRemindersEnabled);

        var kept = await client.PutAsJsonAsync("/notifications/settings", Body(initial, null));
        Assert.False((await kept.Content.ReadFromJsonAsync<NotificationSettingsResponse>(MindflowApiFactory.Json))!.TelegramRemindersEnabled);
    }

    private static object Body(NotificationSettingsResponse s, bool? telegramRemindersEnabled) => new
    {
        s.Enabled, s.MorningBriefEnabled, s.MorningBriefTime, s.MiddayBriefEnabled, s.MiddayBriefTime,
        s.EveningSummaryEnabled, s.EveningSummaryTime, s.BlockRemindersEnabled, s.BlockReminderMinutes,
        telegramRemindersEnabled
    };
}
