using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Tests.Infrastructure;

namespace Mindflow.Api.Tests.Telegram;

public class TelegramAiPlanButtonsTests(TelegramApiFactory factory) : IClassFixture<TelegramApiFactory>
{
    private static long chatSeed = Random.Shared.NextInt64(1_000_000_000_000, 2_000_000_000_000);

    [LocalDatabaseFact]
    public async Task Accept_applies_the_suggestion_once()
    {
        var (_, chatId, taskId, suggestionId) = await ConnectedWithSuggestionAsync();
        var first = $"cb-{Guid.NewGuid():N}";
        var second = $"cb-{Guid.NewGuid():N}";

        await Webhook(Callback(chatId, $"sg:ok:{suggestionId:N}", first));
        await Webhook(Callback(chatId, $"sg:ok:{suggestionId:N}", second));

        Assert.Equal(TaskPriority.P1, await PriorityAsync(taskId));
        Assert.Equal(SuggestionStatus.Accepted, await StatusAsync(suggestionId));
        Assert.Equal("✅ Przyjęto", AnswerFor(first));
        Assert.Equal("Ta sugestia jest już nieaktualna.", AnswerFor(second));
        Assert.True(KeyboardRemoved(chatId));
    }

    [LocalDatabaseFact]
    public async Task Reject_leaves_the_task_untouched()
    {
        var (_, chatId, taskId, suggestionId) = await ConnectedWithSuggestionAsync();
        var callbackId = $"cb-{Guid.NewGuid():N}";

        await Webhook(Callback(chatId, $"sg:no:{suggestionId:N}", callbackId));

        Assert.Equal(TaskPriority.P3, await PriorityAsync(taskId));
        Assert.Equal(SuggestionStatus.Rejected, await StatusAsync(suggestionId));
        Assert.Equal("Odrzucono", AnswerFor(callbackId));
        Assert.True(KeyboardRemoved(chatId));
    }

    [LocalDatabaseFact]
    public async Task Suggestion_of_another_user_is_rejected()
    {
        var (_, _, taskId, suggestionId) = await ConnectedWithSuggestionAsync();
        var (_, strangerChatId, _, _) = await ConnectedWithSuggestionAsync();
        var callbackId = $"cb-{Guid.NewGuid():N}";

        await Webhook(Callback(strangerChatId, $"sg:ok:{suggestionId:N}", callbackId));

        Assert.Equal(TaskPriority.P3, await PriorityAsync(taskId));
        Assert.Equal(SuggestionStatus.Pending, await StatusAsync(suggestionId));
        Assert.Equal("Ta sugestia jest już nieaktualna.", AnswerFor(callbackId));
        Assert.False(KeyboardRemoved(strangerChatId));
    }

    private async Task<(Guid UserId, long ChatId, Guid TaskId, Guid SuggestionId)> ConnectedWithSuggestionAsync()
    {
        var user = await factory.CreateUserAsync();
        var chatId = Interlocked.Increment(ref chatSeed);
        var task = new TaskItem { Id = Guid.NewGuid(), UserId = user.Id, Content = "Raport kwartalny", Priority = TaskPriority.P3, CreatedAt = DateTimeOffset.UtcNow };
        var suggestion = new AiSuggestion
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Title = "Domknij raport",
            Body = "Raport jest po terminie.",
            Status = SuggestionStatus.Pending,
            GeneratedForDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Provider = "Gemini",
            CreatedAt = DateTimeOffset.UtcNow,
            Actions =
            [
                new SuggestionAction
                {
                    Id = Guid.NewGuid(),
                    TaskId = task.Id,
                    ActionType = SuggestionActionType.ChangePriority,
                    Payload = """{"priority":"P1"}""",
                    SortOrder = 0
                }
            ]
        };
        await factory.WithDbAsync(async db =>
        {
            db.TelegramConnections.Add(new TelegramConnection { UserId = user.Id, ChatId = chatId, ConnectedAt = DateTimeOffset.UtcNow });
            db.Tasks.Add(task);
            db.AiSuggestions.Add(suggestion);
            await db.SaveChangesAsync();
        });
        return (user.Id, chatId, task.Id, suggestion.Id);
    }

    private Task<TaskPriority> PriorityAsync(Guid taskId) =>
        factory.WithDbAsync(db => db.Tasks.AsNoTracking().Where(t => t.Id == taskId).Select(t => t.Priority).SingleAsync());

    private Task<SuggestionStatus> StatusAsync(Guid suggestionId) =>
        factory.WithDbAsync(db => db.AiSuggestions.AsNoTracking().Where(s => s.Id == suggestionId).Select(s => s.Status).SingleAsync());

    private static object Callback(long chatId, string data, string id) => new
    {
        update_id = 4,
        callback_query = new
        {
            id,
            from = new { id = chatId, username = "filip" },
            message = new { message_id = 77, chat = new { id = chatId, type = "private" } },
            data
        }
    };

    private Task<HttpResponseMessage> Webhook(object update)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/integrations/telegram/webhook") { Content = JsonContent.Create(update) };
        request.Headers.Add("X-Telegram-Bot-Api-Secret-Token", "hook-secret");
        return factory.CreateClient().SendAsync(request);
    }

    private string AnswerFor(string callbackId)
    {
        lock (factory.Telegram.Requests)
            return (string)factory.Telegram.Requests
                .Last(r => r.Method == "answerCallbackQuery" && (string)r.Body["callback_query_id"]! == callbackId).Body["text"]!;
    }

    private bool KeyboardRemoved(long chatId)
    {
        lock (factory.Telegram.Requests)
            return factory.Telegram.Requests.Any(r => r.Method == "editMessageReplyMarkup"
                && (long)r.Body["chat_id"]! == chatId && (long)r.Body["message_id"]! == 77);
    }
}
