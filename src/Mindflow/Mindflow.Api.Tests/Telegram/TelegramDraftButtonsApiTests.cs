using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Tests.Infrastructure;

namespace Mindflow.Api.Tests.Telegram;

public class TelegramDraftButtonsApiTests(TelegramAiApiFactory factory) : IClassFixture<TelegramAiApiFactory>
{
    private static long chatSeed = Random.Shared.NextInt64(10_000_000_000, 20_000_000_000);

    [LocalDatabaseFact]
    public async Task Add_all_creates_the_tasks_once()
    {
        var (userId, chatId) = await ConnectedAsync();
        var project = await factory.CreateProjectAsync(userId, "Firma");
        var dueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2);
        var draftId = await SeedDraftAsync(userId,
            new DictatedTaskResponse("Wysłać umowę", null, dueDate, new TimeOnly(14, 0), TaskPriority.P2, project.Id, "Firma", []),
            new DictatedTaskResponse("Kupić mleko", null, null, null, TaskPriority.P3, null, null, ["2 litry"]));
        var first = $"cb-{Guid.NewGuid():N}";
        var second = $"cb-{Guid.NewGuid():N}";

        await Webhook(Callback(chatId, $"cap:add:{draftId:N}", first));
        await Webhook(Callback(chatId, $"cap:add:{draftId:N}", second));

        var tasks = await factory.WithDbAsync(db => db.Tasks.AsNoTracking().Include(t => t.Subtasks).Where(t => t.UserId == userId).OrderBy(t => t.Content).ToListAsync());
        Assert.Equal(["Kupić mleko", "Wysłać umowę"], tasks.Select(t => t.Content).ToList());
        Assert.Equal("2 litry", Assert.Single(tasks[0].Subtasks).Content);
        Assert.Equal((project.Id, dueDate, new TimeOnly(14, 0), TaskPriority.P2), (tasks[1].ProjectId!.Value, tasks[1].DueDate!.Value, tasks[1].DueTime!.Value, tasks[1].Priority));
        Assert.Equal(2, await factory.WithDbAsync(db => db.TaskActivityEvents.CountAsync(e => e.UserId == userId && e.EventType == TaskActivityEventType.TaskCreated)));
        Assert.Equal("✅ Dodano 2 zadania", AnswerFor(first));
        Assert.Equal("Ta lista jest już obsłużona.", AnswerFor(second));
        Assert.Equal(TelegramTaskDraftStatus.Added, await StatusAsync(draftId));
        lock (factory.Telegram.Requests)
            Assert.Contains(factory.Telegram.Requests, r => r.Method == "editMessageReplyMarkup"
                && (long)r.Body["chat_id"]! == chatId && (long)r.Body["message_id"]! == 77
                && r.Body["reply_markup"]!["inline_keyboard"]!.AsArray().Count == 0);
    }

    [LocalDatabaseFact]
    public async Task Cancel_creates_nothing()
    {
        var (userId, chatId) = await ConnectedAsync();
        var draftId = await SeedDraftAsync(userId, new DictatedTaskResponse("Kupić mleko", null, null, null, TaskPriority.P3, null, null, []));
        var cancel = $"cb-{Guid.NewGuid():N}";
        var addAfter = $"cb-{Guid.NewGuid():N}";

        await Webhook(Callback(chatId, $"cap:no:{draftId:N}", cancel));
        await Webhook(Callback(chatId, $"cap:add:{draftId:N}", addAfter));

        Assert.False(await factory.WithDbAsync(db => db.Tasks.AnyAsync(t => t.UserId == userId)));
        Assert.Equal("Anulowano", AnswerFor(cancel));
        Assert.Equal("Ta lista jest już obsłużona.", AnswerFor(addAfter));
        Assert.Equal(TelegramTaskDraftStatus.Cancelled, await StatusAsync(draftId));
    }

    [LocalDatabaseFact]
    public async Task Draft_of_another_user_is_rejected()
    {
        var (ownerId, _) = await ConnectedAsync();
        var (strangerId, strangerChatId) = await ConnectedAsync();
        var draftId = await SeedDraftAsync(ownerId, new DictatedTaskResponse("Kupić mleko", null, null, null, TaskPriority.P3, null, null, []));
        var callbackId = $"cb-{Guid.NewGuid():N}";

        await Webhook(Callback(strangerChatId, $"cap:add:{draftId:N}", callbackId));

        Assert.Equal("Ta lista wygasła. Wyślij wiadomość jeszcze raz.", AnswerFor(callbackId));
        Assert.Equal(TelegramTaskDraftStatus.Pending, await StatusAsync(draftId));
        Assert.False(await factory.WithDbAsync(db => db.Tasks.AnyAsync(t => t.UserId == ownerId || t.UserId == strangerId)));
    }

    [LocalDatabaseFact]
    public async Task Expired_draft_is_rejected()
    {
        var (userId, chatId) = await ConnectedAsync();
        var draftId = await SeedDraftAsync(userId, new DictatedTaskResponse("Kupić mleko", null, null, null, TaskPriority.P3, null, null, []));
        await factory.WithDbAsync(db => db.TelegramTaskDrafts.Where(d => d.Id == draftId)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1))));
        var callbackId = $"cb-{Guid.NewGuid():N}";

        await Webhook(Callback(chatId, $"cap:add:{draftId:N}", callbackId));

        Assert.Equal("Ta lista wygasła. Wyślij wiadomość jeszcze raz.", AnswerFor(callbackId));
        Assert.False(await factory.WithDbAsync(db => db.Tasks.AnyAsync(t => t.UserId == userId)));
    }

    private async Task<Guid> SeedDraftAsync(Guid userId, params DictatedTaskResponse[] tasks)
    {
        var draft = new TelegramTaskDraft
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            SourceChatId = Interlocked.Increment(ref chatSeed),
            SourceMessageId = 1,
            Tasks = JsonSerializer.Serialize(tasks, MindflowApiFactory.Json),
            Status = TelegramTaskDraftStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(24)
        };
        await factory.WithDbAsync(async db =>
        {
            db.TelegramTaskDrafts.Add(draft);
            await db.SaveChangesAsync();
        });
        return draft.Id;
    }

    private Task<TelegramTaskDraftStatus> StatusAsync(Guid draftId) =>
        factory.WithDbAsync(db => db.TelegramTaskDrafts.AsNoTracking().Where(d => d.Id == draftId).Select(d => d.Status).SingleAsync());

    private static object Callback(long chatId, string data, string id) => new
    {
        update_id = 2,
        callback_query = new
        {
            id,
            from = new { id = chatId, username = "filip" },
            message = new { message_id = 77, chat = new { id = chatId, type = "private" } },
            data
        }
    };

    private async Task<(Guid UserId, long ChatId)> ConnectedAsync()
    {
        var user = await factory.CreateUserAsync();
        var chatId = Interlocked.Increment(ref chatSeed);
        await factory.WithDbAsync(async db =>
        {
            db.TelegramConnections.Add(new TelegramConnection { UserId = user.Id, ChatId = chatId, ConnectedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        });
        return (user.Id, chatId);
    }

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
}
