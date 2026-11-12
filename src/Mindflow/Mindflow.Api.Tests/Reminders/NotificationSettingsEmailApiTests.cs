using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Tests.Infrastructure;

namespace Mindflow.Api.Tests.Reminders;

public class NotificationSettingsEmailApiTests(MindflowApiFactory factory) : IClassFixture<MindflowApiFactory>
{
    [LocalDatabaseFact]
    public async Task Email_reminders_are_on_by_default_and_can_be_switched_off()
    {
        var user = await factory.CreateUserAsync();
        var client = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Email:Smtp:Host"] = "",
                ["Email:Smtp:From"] = ""
            }))).CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MindflowApiFactory.CreateUserJwt(user.Id));

        var initial = await client.GetFromJsonAsync<NotificationSettingsResponse>("/notifications/settings", MindflowApiFactory.Json);
        Assert.True(initial!.EmailRemindersEnabled);
        Assert.False(initial.EmailConfigured);

        var response = await client.PutAsJsonAsync("/notifications/settings", Body(initial, emailRemindersEnabled: false));
        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<NotificationSettingsResponse>(MindflowApiFactory.Json);
        Assert.False(updated!.EmailRemindersEnabled);
    }

    [LocalDatabaseFact]
    public async Task Old_clients_without_the_field_keep_the_value()
    {
        var user = await factory.CreateUserAsync();
        var client = factory.CreateUserClient(user.Id);
        var initial = await client.GetFromJsonAsync<NotificationSettingsResponse>("/notifications/settings", MindflowApiFactory.Json);
        await client.PutAsJsonAsync("/notifications/settings", Body(initial!, emailRemindersEnabled: false));

        var response = await client.PutAsJsonAsync("/notifications/settings", Body(initial!, emailRemindersEnabled: null));

        var updated = await response.Content.ReadFromJsonAsync<NotificationSettingsResponse>(MindflowApiFactory.Json);
        Assert.False(updated!.EmailRemindersEnabled);
    }

    private static object Body(NotificationSettingsResponse s, bool? emailRemindersEnabled) => new
    {
        s.Enabled, s.MorningBriefEnabled, s.MorningBriefTime, s.MiddayBriefEnabled, s.MiddayBriefTime,
        s.EveningSummaryEnabled, s.EveningSummaryTime, s.BlockRemindersEnabled, s.BlockReminderMinutes,
        emailRemindersEnabled
    };
}
