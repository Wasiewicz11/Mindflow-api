using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Services.Telegram;
using Mindflow.Api.Tests.Infrastructure;
using TaskStatus = Mindflow.Api.Models.Enums.TaskStatus;

namespace Mindflow.Api.Tests.Telegram;

public class TelegramButtonsApiTests(TelegramApiFactory factory) : IClassFixture<TelegramApiFactory>
{
    private static long chatSeed = Random.Shared.NextInt64(10_000_000, 90_000_000);

    [LocalDatabaseFact]
    public async Task Done_button_completes_the_task()
    {
        var (_, chatId, taskId) = await ConnectedWithTaskAsync();

        await Webhook(Callback(chatId, $"done:{taskId:N}"));

        var task = await factory.WithDbAsync(db => db.Tasks.AsNoTracking().SingleAsync(t => t.Id == taskId));
        Assert.True(task.IsCompleted);
        Assert.Equal(TaskStatus.Completed, task.Status);
        Assert.Equal("✅ Ukończono", AnswerFor("cb-1"));
        Assert.Contains(factory.Telegram.Requests, r => r.Method == "sendMessage" && (long)r.Body["chat_id"]! == chatId && (string)r.Body["text"]! == "✅ Ukończono: Telefon &amp; mail");
    }

    [LocalDatabaseFact]
    public async Task Snooze_button_sets_snooze_window()
    {
        var (_, chatId, taskId) = await ConnectedWithTaskAsync();
        var before = DateTimeOffset.UtcNow;

        await Webhook(Callback(chatId, $"snooze:{taskId:N}"));

        var after = DateTimeOffset.UtcNow;
        var task = await factory.WithDbAsync(db => db.Tasks.AsNoTracking().SingleAsync(t => t.Id == taskId));
        Assert.InRange(task.ReminderSnoozedUntil!.Value, before.AddMinutes(10), after.AddMinutes(10));
        Assert.Equal("⏰ Przypomnę za 10 min", AnswerFor("cb-1"));
    }

    [LocalDatabaseFact]
    public async Task Button_for_another_users_task_is_rejected()
    {
        var (_, chatId, _) = await ConnectedWithTaskAsync();
        var (_, _, foreignTaskId) = await ConnectedWithTaskAsync();

        await Webhook(Callback(chatId, $"done:{foreignTaskId:N}"));

        var task = await factory.WithDbAsync(db => db.Tasks.AsNoTracking().SingleAsync(t => t.Id == foreignTaskId));
        Assert.False(task.IsCompleted);
        Assert.Equal("Nie znaleziono zadania", AnswerFor("cb-1"));
    }

    [LocalDatabaseFact]
    public async Task Button_from_unlinked_chat_asks_to_connect()
    {
        await Webhook(Callback(Interlocked.Increment(ref chatSeed), $"done:{Guid.NewGuid():N}"));

        Assert.Equal("Najpierw połącz konto w Mindflow", AnswerFor("cb-1"));
    }

    [LocalDatabaseFact]
    public async Task Polling_handles_updates_and_advances_offset()
    {
        var user = await factory.CreateUserAsync();
        var client = factory.CreateUserClient(user.Id);
        var chatId = Interlocked.Increment(ref chatSeed);
        var link = await (await client.PostAsync("/integrations/telegram/link", null)).Content.ReadFromJsonAsync<TelegramLinkResponse>(MindflowApiFactory.Json);
        var code = link!.Url.Split("start=")[1];
        factory.Telegram.UpdatesResults.Enqueue(System.Text.Json.JsonSerializer.Serialize(new[]
        {
            new { update_id = 41, message = new { message_id = 1, chat = new { id = chatId }, from = new { id = chatId, username = "filip" }, text = $"/start {code}" } }
        }));
        var polling = ActivatorUtilities.CreateInstance<TelegramPollingService>(factory.Services);

        var offset = await polling.PollOnceAsync(0, CancellationToken.None);

        Assert.Equal(42, offset);
        var status = await client.GetFromJsonAsync<TelegramStatusResponse>("/integrations/telegram", MindflowApiFactory.Json);
        Assert.True(status!.Connected);
    }

    private string AnswerFor(string callbackId)
    {
        lock (factory.Telegram.Requests)
            return (string)factory.Telegram.Requests
                .Last(r => r.Method == "answerCallbackQuery" && (string)r.Body["callback_query_id"]! == callbackId).Body["text"]!;
    }

    private async Task<(Guid UserId, long ChatId, Guid TaskId)> ConnectedWithTaskAsync()
    {
        var user = await factory.CreateUserAsync();
        var client = factory.CreateUserClient(user.Id);
        var chatId = Interlocked.Increment(ref chatSeed);
        var link = await (await client.PostAsync("/integrations/telegram/link", null)).Content.ReadFromJsonAsync<TelegramLinkResponse>(MindflowApiFactory.Json);
        await Webhook(new
        {
            update_id = 1,
            message = new { message_id = 1, chat = new { id = chatId }, from = new { id = chatId, username = "filip" }, text = $"/start {link!.Url.Split("start=")[1]}" }
        });

        var task = new TaskItem { Id = Guid.NewGuid(), UserId = user.Id, Content = "Telefon & mail", CreatedAt = DateTimeOffset.UtcNow };
        await factory.WithDbAsync(async db =>
        {
            db.Tasks.Add(task);
            await db.SaveChangesAsync();
        });
        return (user.Id, chatId, task.Id);
    }

    private static object Callback(long chatId, string data) => new
    {
        update_id = 2,
        callback_query = new { id = "cb-1", from = new { id = chatId, username = "filip" }, message = new { message_id = 5, chat = new { id = chatId } }, data }
    };

    private Task<HttpResponseMessage> Webhook(object update)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/integrations/telegram/webhook") { Content = JsonContent.Create(update) };
        request.Headers.Add("X-Telegram-Bot-Api-Secret-Token", "hook-secret");
        return factory.CreateClient().SendAsync(request);
    }
}
