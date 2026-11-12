using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Mindflow.Api.Services;
using Mindflow.Api.Services.Reminders;

namespace Mindflow.Api.Tests.Reminders;

public class PushReminderChannelTests
{
    private sealed class CapturingSender : IWebPushSender
    {
        public object? Payload { get; private set; }

        public Task<bool> SendAsync(Guid userId, object payload, CancellationToken ct)
        {
            Payload = payload;
            return Task.FromResult(true);
        }
    }

    [Fact]
    public async Task Payload_carries_the_shared_notification_id_and_action_data()
    {
        var sender = new CapturingSender();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["App:ApiBaseUrl"] = "https://api.test" }).Build();
        var channel = new PushReminderChannel(sender, configuration);
        var dueAt = new DateTimeOffset(2030, 1, 10, 13, 0, 0, TimeSpan.Zero);
        var message = new ReminderMessage(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "⏰ X", "Teraz", "dziś 14:00", dueAt, "https://app/zadania/1", "tok");

        Assert.True(await channel.SendAsync(message, CancellationToken.None));

        var json = JsonSerializer.SerializeToElement(sender.Payload, JsonSerializerOptions.Web);
        Assert.Equal("reminder", json.GetProperty("kind").GetString());
        Assert.Equal(message.NotificationId, json.GetProperty("notificationId").GetGuid());
        Assert.Equal(dueAt, json.GetProperty("dueAt").GetDateTimeOffset());
        Assert.Equal("tok", json.GetProperty("actionToken").GetString());
        Assert.Equal(ReminderLinks.ActionApiUrl(configuration), json.GetProperty("actionUrl").GetString());
        Assert.True(json.GetProperty("requireInteraction").GetBoolean());
        Assert.True(json.GetProperty("renotify").GetBoolean());
        Assert.Equal(message.TaskId, json.GetProperty("taskId").GetGuid());
        Assert.Equal($"reminder:{message.TaskId:N}", json.GetProperty("tag").GetString());
    }
}
