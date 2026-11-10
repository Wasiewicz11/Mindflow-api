using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Tests.Infrastructure;

namespace Mindflow.Api.Tests.Telegram;

public class TelegramTaskCaptureApiTests(TelegramAiApiFactory factory) : IClassFixture<TelegramAiApiFactory>
{
    private static readonly TimeZoneInfo Warsaw = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");
    private static long chatSeed = Random.Shared.NextInt64(3_000_000_000, 4_000_000_000);

    [LocalDatabaseFact]
    public async Task Text_message_becomes_a_draft_with_buttons()
    {
        var (userId, chatId) = await ConnectedAsync();
        await factory.CreateProjectAsync(userId, "Firma");
        var tomorrow = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Warsaw).DateTime).AddDays(1);
        string? sentBody = null;
        factory.Ai.Respond((_, body) =>
        {
            sentBody = body;
            return Gemini($$"""{"transcript":"","tasks":[{"title":"Wysłać umowę","priority":"P2","project":"Firma","dueDate":"{{tomorrow:yyyy-MM-dd}}","dueTime":"14:00","subtasks":[]}]}""");
        });

        await Webhook(Text(chatId, "Umowa jutro o 14 dla firmy"));

        Assert.Contains("Umowa jutro o 14 dla firmy", sentBody);
        var draft = await factory.WithDbAsync(db => db.TelegramTaskDrafts.AsNoTracking().SingleAsync(d => d.UserId == userId));
        Assert.Equal(TelegramTaskDraftStatus.Pending, draft.Status);
        Assert.Equal(TimeSpan.FromHours(24), draft.ExpiresAt - draft.CreatedAt);
        var message = Sent(chatId).Last();
        Assert.Equal(
            $"<b>📝 Rozpoznane zadania (1)</b>\n1. <b>Wysłać umowę</b>\n📅 {tomorrow:dd.MM.yyyy} 14:00 · 📁 Firma · P2",
            (string)message["text"]!);
        var row = message["reply_markup"]!["inline_keyboard"]![0]!.AsArray();
        Assert.Equal(("Dodaj wszystkie", $"cap:add:{draft.Id:N}"), ((string)row[0]!["text"]!, (string)row[0]!["callback_data"]!));
        Assert.Equal(("Anuluj", $"cap:no:{draft.Id:N}"), ((string)row[1]!["text"]!, (string)row[1]!["callback_data"]!));
        Assert.False(await factory.WithDbAsync(db => db.Tasks.AnyAsync(t => t.UserId == userId)));
    }

    [LocalDatabaseFact]
    public async Task Message_without_tasks_gets_a_short_answer()
    {
        var (userId, chatId) = await ConnectedAsync();
        factory.Ai.Respond((_, _) => Gemini("""{"transcript":"","tasks":[]}"""));

        await Webhook(Text(chatId, "Dzien dobry"));

        Assert.Equal("Nie znalazłem zadań w tej wiadomości.", (string)Sent(chatId).Last()["text"]!);
        Assert.False(await factory.WithDbAsync(db => db.TelegramTaskDrafts.AnyAsync(d => d.UserId == userId)));
    }

    [LocalDatabaseFact]
    public async Task Daily_limit_is_reported_in_polish()
    {
        var (_, chatId) = await ConnectedAsync();
        factory.Ai.Respond((_, _) => Gemini("""{"transcript":"","tasks":[]}"""));
        var before = factory.Ai.Requests.Count;

        for (var i = 0; i < 4; i++)
            await Webhook(Text(chatId, "Kupic mleko"));

        Assert.Equal(3, factory.Ai.Requests.Count - before);
        Assert.Equal("Wykorzystano dzienny limit AI. Spróbuj jutro.", (string)Sent(chatId).Last()["text"]!);
    }

    [LocalDatabaseFact]
    public async Task Ai_error_is_reported_without_details()
    {
        var (_, chatId) = await ConnectedAsync();
        factory.Ai.Respond((_, _) => FakeAiHandler.Json("""{"error":{"message":"boom"}}""", System.Net.HttpStatusCode.InternalServerError));

        await Webhook(Text(chatId, "Kupic mleko"));

        Assert.Equal("Nie udało się teraz rozpoznać zadań. Spróbuj za chwilę.", (string)Sent(chatId).Last()["text"]!);
    }

    [LocalDatabaseFact]
    public async Task Unlinked_or_group_chat_gets_connect_instructions()
    {
        var (_, linkedChatId) = await ConnectedAsync();
        var strangerChatId = Interlocked.Increment(ref chatSeed);
        var before = factory.Ai.Requests.Count;

        await Webhook(Text(strangerChatId, "Kupic mleko"));
        await Webhook(Text(linkedChatId, "Kupic mleko", chatType: "group"));

        Assert.Equal(before, factory.Ai.Requests.Count);
        Assert.Equal("Połącz konto w Mindflow: Ustawienia → Powiadomienia → Telegram.", (string)Sent(strangerChatId).Last()["text"]!);
        Assert.Equal("Połącz konto w Mindflow: Ustawienia → Powiadomienia → Telegram.", (string)Sent(linkedChatId).Last()["text"]!);
    }

    [LocalDatabaseFact]
    public async Task Retried_update_is_skipped()
    {
        var (userId, chatId) = await ConnectedAsync();
        factory.Ai.Respond((_, _) => Gemini("""{"transcript":"","tasks":[{"title":"Kupic mleko","priority":"P3","subtasks":[]}]}"""));
        var before = factory.Ai.Requests.Count;

        await Webhook(Text(chatId, "Kupic mleko"));
        await Webhook(Text(chatId, "Kupic mleko"));

        Assert.Equal(1, factory.Ai.Requests.Count - before);
        Assert.Single(Sent(chatId));
        Assert.Equal(1, await factory.WithDbAsync(db => db.TelegramTaskDrafts.CountAsync(d => d.UserId == userId)));
    }

    private static HttpResponseMessage Gemini(string json) =>
        FakeAiHandler.Json(JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new[] { new { text = json } } } } } }));

    private static object Text(long chatId, string text, string chatType = "private") => new
    {
        update_id = 1,
        message = new { message_id = 10, chat = new { id = chatId, type = chatType }, from = new { id = chatId, username = "filip" }, text }
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

    private List<JsonObject> Sent(long chatId)
    {
        lock (factory.Telegram.Requests)
            return factory.Telegram.Requests
                .Where(r => r.Method == "sendMessage" && (long?)r.Body["chat_id"] == chatId)
                .Select(r => r.Body)
                .ToList();
    }
    [LocalDatabaseFact]
    public async Task Voice_message_is_downloaded_and_sent_as_ogg()
    {
        var (userId, chatId) = await ConnectedAsync();
        var fileId = $"voice-{Guid.NewGuid():N}";
        var audio = Enumerable.Range(0, 2048).Select(i => (byte)i).ToArray();
        factory.Telegram.Files[fileId] = ($"voice/{fileId}.oga", audio);
        string? sentBody = null;
        factory.Ai.Respond((_, body) =>
        {
            sentBody = body;
            return Gemini("""{"transcript":"Kupić mleko.","tasks":[{"title":"Kupić mleko","priority":"P3","subtasks":[]}]}""");
        });

        await Webhook(Voice(chatId, fileId, duration: 12, size: audio.Length));

        using var doc = JsonDocument.Parse(sentBody!);
        var inline = doc.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("inline_data");
        Assert.Equal("audio/ogg", inline.GetProperty("mime_type").GetString());
        Assert.Equal(Convert.ToBase64String(audio), inline.GetProperty("data").GetString());
        Assert.True(await factory.WithDbAsync(db => db.TelegramTaskDrafts.AnyAsync(d => d.UserId == userId)));
        Assert.StartsWith("<b>📝 Rozpoznane zadania (1)</b>", (string)Sent(chatId).Last()["text"]!);
    }

    [LocalDatabaseFact]
    public async Task Too_long_voice_is_rejected_before_download()
    {
        var (_, chatId) = await ConnectedAsync();
        var fileId = $"voice-{Guid.NewGuid():N}";
        factory.Telegram.Files[fileId] = ($"voice/{fileId}.oga", new byte[16]);
        var before = factory.Ai.Requests.Count;

        await Webhook(Voice(chatId, fileId, duration: 181, size: 16));

        Assert.Equal("Głosówka może mieć najwyżej 3 minuty.", (string)Sent(chatId).Last()["text"]!);
        Assert.Equal(before, factory.Ai.Requests.Count);
        lock (factory.Telegram.Requests)
            Assert.DoesNotContain(factory.Telegram.Requests, r => r.Method == "getFile" && (string?)r.Body["file_id"] == fileId);
    }

    [LocalDatabaseFact]
    public async Task Failed_download_is_reported()
    {
        var (_, chatId) = await ConnectedAsync();
        var before = factory.Ai.Requests.Count;

        await Webhook(Voice(chatId, $"voice-{Guid.NewGuid():N}", duration: 5, size: 100));

        Assert.Equal("Nie udało się pobrać głosówki. Spróbuj jeszcze raz.", (string)Sent(chatId).Last()["text"]!);
        Assert.Equal(before, factory.Ai.Requests.Count);
    }

    private static object Voice(long chatId, string fileId, int duration, long size) => new
    {
        update_id = 3,
        message = new
        {
            message_id = 11,
            chat = new { id = chatId, type = "private" },
            from = new { id = chatId, username = "filip" },
            voice = new { file_id = fileId, file_unique_id = $"u-{fileId}", duration, mime_type = "audio/ogg", file_size = size }
        }
    };
}
