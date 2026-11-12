# Dodawanie zadań z bota Telegrama (API) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use subagent-driven-development (recommended) or executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wiadomość tekstowa albo głosówka wysłana do bota z połączonego prywatnego czatu przechodzi przez istniejący silnik dyktowania AI, bot pokazuje listę rozpoznanych zadań z przyciskami `Dodaj wszystkie` / `Anuluj`, a „Dodaj wszystkie” tworzy zadania raz, w imieniu właściciela czatu.

**Architecture:** `TelegramService.HandleUpdateAsync` kieruje zwykłe wiadomości (nie komendy) i głosówki z połączonego prywatnego czatu do nowego `TelegramTaskCapture`. Ten podszywa się pod właściciela czatu (`TelegramActor.ActAs`, wzór z `ReminderActionService.ActAs`), woła `IDictationService.ProcessAsync` (te same limity i konfiguracja AI co `POST /ai/dictation`), zapisuje szkic w nowej tabeli `telegram_task_drafts` i odpisuje listą z przyciskami. Przycisk atomowo zmienia status szkicu (`ExecuteUpdateAsync` z warunkiem `Pending`), więc drugie kliknięcie niczego nie dubluje, a zadania powstają przez `ITaskService.CreateAsync` (aktywność i SignalR jak przy zwykłym dodawaniu). `TelegramApi` dostaje `getFile`, pobranie pliku i `editMessageReplyMarkup`.

**Tech Stack:** .NET 10, EF Core (Postgres, snake_case, `jsonb`), `TelegramApi` (typowany `HttpClient` z `RemoveAllLoggers`), istniejące `IDictationService`/`ITaskService`, xUnit + WebApplicationFactory (nowa `TelegramAiApiFactory` = AI + Telegram).

**Spec:** `/Users/filipwasiewicz/Projekty/Mindflow/mindflow-api-reminders/docs/superpowers/specs/2026-11-09-telegram-asystent-design.md` (sekcja 2)

**Wymaga:** `docs/superpowers/plans/2026-11-09-telegram-briefs.md` (gałąź `feature/telegram-briefs`). Ten plan nie używa kodu z briefów wprost, ale gałąź wychodzi od niej, a plan `telegram-ai-plan` potrzebuje obu.

## Global Constraints

- Worktree `/Users/filipwasiewicz/Projekty/Mindflow/mindflow-api-reminders`. Gałąź: `git switch -c feature/telegram-task-capture feature/telegram-briefs`. MR zależy od MR `feature/telegram-briefs`.
- Commity: Conventional Commits po angielsku, małą literą, tryb rozkazujący, bez opisu, bez `Co-Authored-By`, bez wzmianek o AI. Test w tym samym commicie co kod. Zero komentarzy „dlaczego” w kodzie.
- Teksty bota po polsku, dokładnie (stałe w `TelegramTaskCapture`):
  - nagłówek listy: `<b>📝 Rozpoznane zadania ({N})</b>`, potem dla każdego zadania (maks. 15): `{i}. <b>{tytuł, maks. 80 znaków}</b>` i wiersz `📅 {dd.MM.yyyy}[ {HH:mm}] · 📁 {projekt} · {P1..P4}` (bez daty: `📅 bez terminu`, bez projektu: `📁 bez projektu`); przy więcej niż 15: `… i jeszcze {N-15}`. Wszystkie wartości przez `WebUtility.HtmlEncode`.
  - przyciski: `Dodaj wszystkie` (`cap:add:{id:N}`), `Anuluj` (`cap:no:{id:N}`) w jednym wierszu.
  - brak zadań: `Nie znalazłem zadań w tej wiadomości.`
  - dzienny limit: `Wykorzystano dzienny limit AI. Spróbuj jutro.`
  - inny błąd AI: `Nie udało się teraz rozpoznać zadań. Spróbuj za chwilę.`
  - za długa głosówka: `Głosówka może mieć najwyżej 3 minuty.`
  - nieudane pobranie: `Nie udało się pobrać głosówki. Spróbuj jeszcze raz.`
  - odpowiedzi na przyciski (`answerCallbackQuery`): `✅ Dodano {N} zadanie|zadania|zadań` (polska odmiana), `Nie udało się dodać zadań.` (gdy 0), `Anulowano`, `Ta lista jest już obsłużona.`, `Ta lista wygasła. Wyślij wiadomość jeszcze raz.`
  - niepołączony albo nieprywatny czat: istniejący `Połącz konto w Mindflow: Ustawienia → Powiadomienia → Telegram.`
- Po obsłużeniu przycisku bot zdejmuje klawiaturę (`editMessageReplyMarkup` z pustym `inline_keyboard`).
- Głosówka: `voice.duration` ≤ 180 s (jak `MAX_RECORDING_MS` w webie) i rozmiar ≤ `Ai:Dictation:MaxAudioBytes` (8 MB). Typ `voice.mime_type` przez `AudioFormats.Normalize`, domyślnie `audio/ogg`.
- Token bota nigdy w logach: pobranie pliku idzie przez ten sam `HttpClient` (`RemoveAllLoggers`), logujemy tylko status albo typ wyjątku, nigdy URL ani `file_path`.
- Szkic: tabela `telegram_task_drafts` (`id`, `user_id` FK z kaskadą, `tasks jsonb`, `status varchar(16)`, `created_at`, `expires_at` = +24 h), indeks na `user_id`. Przy zapisie nowego szkicu usuwamy przeterminowane szkice tego użytkownika.
- Testy DB: `ConnectionStrings__Database='Host=localhost;Port=5435;Database=mindflow_reminders_tests;Username=postgres;Password=postgres'`. Fabryka nie migruje sama: po migracji uruchom `dotnet ef database update` z tym connection stringiem.
- Rozmiar gałęzi: ≲ 20 plików i 1000 linii (bez migracji i `.md`). Ten plan: 15 plików kodu i testów.
- Build: `dotnet build src/Mindflow/Mindflow.sln`. Testy: `ConnectionStrings__Database='…' dotnet test src/Mindflow/Mindflow.sln`.

---

### Task 1: Bot API — głosówki, `getFile`, pobranie pliku, zdjęcie klawiatury

**Files:**
- Modify: `src/Mindflow/Mindflow.Api/Services/Telegram/TelegramModels.cs`
- Modify: `src/Mindflow/Mindflow.Api/Services/Telegram/TelegramApi.cs`
- Modify: `src/Mindflow/Mindflow.Api.Tests/Infrastructure/FakeTelegramHandler.cs`
- Test: `src/Mindflow/Mindflow.Api.Tests/Telegram/TelegramApiTests.cs`, `src/Mindflow/Mindflow.Api.Tests/Telegram/TelegramLoggingTests.cs`

**Interfaces:**
- Consumes: prywatne `TelegramApi.CallAsync<T>(string method, object body, CancellationToken ct)`, `TelegramOptions.From`.
- Produces:

```csharp
public record TelegramMessage(long MessageId, TelegramChat Chat, TelegramUser? From, string? Text, TelegramVoice? Voice = null);
public record TelegramVoice(string FileId, int Duration, string? MimeType = null, long? FileSize = null);
public record TelegramFile(string FileId, string? FilePath, long? FileSize);
```

```csharp
public Task<TelegramFile?> GetFileAsync(string fileId, CancellationToken ct);
public Task<byte[]?> DownloadFileAsync(string filePath, long maxBytes, CancellationToken ct);
public Task<bool> RemoveKeyboardAsync(long chatId, long messageId, CancellationToken ct);
```

`DownloadFileAsync` robi `GET file/bot{token}/{filePath}` i zwraca `null`, gdy bot nie jest skonfigurowany, odpowiedź nie jest 2xx, plik przekracza `maxBytes` albo poleciał wyjątek sieciowy. `RemoveKeyboardAsync` woła `editMessageReplyMarkup` z `{ chat_id, message_id, reply_markup: { inline_keyboard: [] } }` i deserializuje wynik jako `JsonElement` (Telegram zwraca tu obiekt wiadomości, nie `true`).

`FakeTelegramHandler` (używany przez Taski 2–4 i plan `telegram-ai-plan`):

```csharp
public Dictionary<string, (string Path, byte[] Bytes)> Files { get; } = new();
```

Klucz to `file_id`. `getFile` dla znanego `file_id` zwraca `{ file_id, file_path, file_size }`, dla nieznanego 400 z `ok:false`. `GET /file/bot…/{path}` zwraca bajty pliku o tej ścieżce albo 404.

- [ ] **Step 1: Rozszerz `FakeTelegramHandler`** (cała nowa treść pliku):

```csharp
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mindflow.Api.Tests.Infrastructure;

public sealed class FakeTelegramHandler : HttpMessageHandler
{
    public List<Uri> Uris { get; } = [];
    public List<(string Method, JsonObject Body)> Requests { get; } = [];
    public Queue<string> UpdatesResults { get; } = new();
    public Dictionary<string, (string Path, byte[] Bytes)> Files { get; } = new();
    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
    public HashSet<string> FailingMethods { get; } = [];
    public Exception? Throw { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (Throw is not null) throw Throw;
        var segments = request.RequestUri!.AbsolutePath.Split('/');
        if (request.Method == HttpMethod.Get && segments is [_, "file", ..])
            return Download(request.RequestUri, string.Join('/', segments.Skip(3)));

        var method = segments.Last();
        var body = request.Content is null ? new JsonObject() : JsonNode.Parse(await request.Content.ReadAsStringAsync(ct))!.AsObject();
        lock (Requests) Uris.Add(request.RequestUri!);
        lock (Requests) Requests.Add((method, body));
        if (method == "getFile" && !Files.ContainsKey((string?)body["file_id"] ?? ""))
            return Json(HttpStatusCode.BadRequest, """{"ok":false,"description":"Bad Request: invalid file_id"}""");

        var result = method switch
        {
            "getUpdates" => UpdatesResults.TryDequeue(out var next) ? next : "[]",
            "getFile" => FileResult((string)body["file_id"]!),
            _ => "true"
        };
        return Json(FailingMethods.Contains(method) ? HttpStatusCode.Unauthorized : Status, $$"""{"ok":true,"result":{{result}}}""");
    }

    private string FileResult(string fileId) =>
        JsonSerializer.Serialize(new { file_id = fileId, file_path = Files[fileId].Path, file_size = Files[fileId].Bytes.Length });

    private HttpResponseMessage Download(Uri uri, string path)
    {
        lock (Requests) Uris.Add(uri);
        var file = Files.Values.FirstOrDefault(f => f.Path == path);
        return file.Bytes is null
            ? Json(HttpStatusCode.NotFound, """{"ok":false}""")
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(file.Bytes) };
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
```

- [ ] **Step 2: Write the failing tests**

Dopisz do `TelegramApiTests` (klasa bez bazy, helper `Create()` już jest):

```csharp
    [Fact]
    public async Task Parses_voice_messages()
    {
        var (api, handler) = Create();
        handler.UpdatesResults.Enqueue("""[{"update_id":8,"message":{"message_id":2,"chat":{"id":42,"type":"private"},"voice":{"file_id":"v1","file_unique_id":"u1","duration":12,"mime_type":"audio/ogg","file_size":2048}}}]""");

        var voice = Assert.Single((await api.GetUpdatesAsync(0, 0, CancellationToken.None))!).Message!.Voice!;

        Assert.Equal(("v1", 12, "audio/ogg", 2048L), (voice.FileId, voice.Duration, voice.MimeType, voice.FileSize!.Value));
    }

    [Fact]
    public async Task Downloads_a_file_found_by_get_file()
    {
        var (api, handler) = Create();
        handler.Files["v1"] = ("voice/file_1.oga", [1, 2, 3]);

        var file = await api.GetFileAsync("v1", CancellationToken.None);
        var bytes = await api.DownloadFileAsync(file!.FilePath!, 10, CancellationToken.None);

        Assert.Equal(("voice/file_1.oga", 3L), (file.FilePath, file.FileSize!.Value));
        Assert.Equal(new byte[] { 1, 2, 3 }, bytes);
        Assert.Equal("https://api.telegram.org/file/bot123:abc/voice/file_1.oga", handler.Uris.Last().ToString());
    }

    [Fact]
    public async Task Unknown_file_and_oversized_download_give_null()
    {
        var (api, handler) = Create();
        handler.Files["big"] = ("voice/big.oga", new byte[11]);

        Assert.Null(await api.GetFileAsync("missing", CancellationToken.None));
        Assert.Null(await api.DownloadFileAsync("voice/big.oga", 10, CancellationToken.None));
        Assert.Null(await api.DownloadFileAsync("voice/missing.oga", 10, CancellationToken.None));
    }

    [Fact]
    public async Task Removes_inline_keyboard()
    {
        var (api, handler) = Create();

        Assert.True(await api.RemoveKeyboardAsync(42, 7, CancellationToken.None));

        var (method, body) = Assert.Single(handler.Requests);
        Assert.Equal("editMessageReplyMarkup", method);
        Assert.Equal((42L, 7L), ((long)body["chat_id"]!, (long)body["message_id"]!));
        Assert.Empty(body["reply_markup"]!["inline_keyboard"]!.AsArray());
    }
```

Dopisz do `TelegramLoggingTests` drugi test (ta sama konfiguracja co `Bot_token_never_reaches_the_logs`):

```csharp
    [LocalDatabaseFact]
    public async Task Failed_file_download_never_logs_the_token()
    {
        var handler = new FakeTelegramHandler();
        var logs = new CapturingLoggerProvider();
        using var app = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Telegram:BotToken"] = "123:abc",
                ["Telegram:BotUsername"] = "mindflow_test_bot"
            }));
            builder.ConfigureLogging(l => { l.ClearProviders(); l.SetMinimumLevel(LogLevel.Trace); l.AddProvider(logs); l.AddFilter<CapturingLoggerProvider>(null, LogLevel.Trace); });
            builder.ConfigureTestServices(services =>
                services.AddHttpClient<TelegramApi>().ConfigurePrimaryHttpMessageHandler(() => handler));
        });
        using var scope = app.Services.CreateScope();

        var bytes = await scope.ServiceProvider.GetRequiredService<TelegramApi>().DownloadFileAsync("voice/missing.oga", 1024, CancellationToken.None);

        Assert.Null(bytes);
        Assert.Contains(logs.Messages, m => m == "Telegram file download failed with status 404");
        Assert.DoesNotContain(logs.Messages, m => m.Contains("123:abc") || m.Contains("voice/missing.oga"));
    }
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `ConnectionStrings__Database='…' dotnet test src/Mindflow/Mindflow.sln --filter "TelegramApiTests|TelegramLoggingTests"`
Expected: błąd kompilacji (`Voice`, `GetFileAsync`, `DownloadFileAsync`, `RemoveKeyboardAsync` nie istnieją).

- [ ] **Step 4: Write minimal implementation**

`TelegramModels.cs` — zamień rekord `TelegramMessage` i dopisz dwa nowe:

```csharp
public record TelegramMessage(long MessageId, TelegramChat Chat, TelegramUser? From, string? Text, TelegramVoice? Voice = null);
public record TelegramVoice(string FileId, int Duration, string? MimeType = null, long? FileSize = null);
public record TelegramFile(string FileId, string? FilePath, long? FileSize);
```

`TelegramApi.cs` — dopisz metody (przed `CallAsync`):

```csharp
    public async Task<TelegramFile?> GetFileAsync(string fileId, CancellationToken ct)
    {
        var response = await CallAsync<TelegramFile>("getFile", new { file_id = fileId }, ct);
        return response.Ok ? response.Result : null;
    }

    public async Task<byte[]?> DownloadFileAsync(string filePath, long maxBytes, CancellationToken ct)
    {
        var options = TelegramOptions.From(configuration);
        if (options is null) return null;

        try
        {
            using var response = await http.GetAsync($"/file/bot{options.BotToken}/{filePath}", HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Telegram file download failed with status {Status}", (int)response.StatusCode);
                return null;
            }

            if (response.Content.Headers.ContentLength > maxBytes) return null;

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, ct)) > 0)
            {
                if (buffer.Length + read > maxBytes) return null;
                buffer.Write(chunk, 0, read);
            }

            return buffer.ToArray();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException || ex is OperationCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Telegram file download failed with {ExceptionType}", ex.GetType().Name);
            return null;
        }
    }

    public async Task<bool> RemoveKeyboardAsync(long chatId, long messageId, CancellationToken ct) =>
        (await CallAsync<JsonElement>("editMessageReplyMarkup",
            new { chat_id = chatId, message_id = messageId, reply_markup = new { inline_keyboard = Array.Empty<object>() } }, ct)).Ok;
```

`JsonElement` jest już dostępny przez `using System.Text.Json;`.

- [ ] **Step 5: Run tests to verify they pass**

Run: `ConnectionStrings__Database='…' dotnet test src/Mindflow/Mindflow.sln --filter "FullyQualifiedName~Telegram"`
Expected: PASS (5 nowych + wszystkie dotychczasowe testy Telegrama, w tym `TelegramButtonsApiTests`, które korzystają z przebudowanego fake'a).

- [ ] **Step 6: Commit**

```bash
git add src/Mindflow/Mindflow.Api/Services/Telegram/TelegramModels.cs \
  src/Mindflow/Mindflow.Api/Services/Telegram/TelegramApi.cs \
  src/Mindflow/Mindflow.Api.Tests/Infrastructure/FakeTelegramHandler.cs \
  src/Mindflow/Mindflow.Api.Tests/Telegram/TelegramApiTests.cs \
  src/Mindflow/Mindflow.Api.Tests/Telegram/TelegramLoggingTests.cs
git commit -m "feat: download telegram voice files and remove keyboards"
```

---

### Task 2: Szkic zadań z wiadomości tekstowej

**Files:**
- Create: `src/Mindflow/Mindflow.Api/Models/TelegramTaskDraft.cs`
- Create: `src/Mindflow/Mindflow.Api/Models/Enums/TelegramTaskDraftStatus.cs`
- Modify: `src/Mindflow/Mindflow.Api/Data/MindflowDbContext.cs`
- Create (generated): migracja `AddTelegramTaskDrafts` (+ `Designer.cs`, `MindflowDbContextModelSnapshot.cs`)
- Create: `src/Mindflow/Mindflow.Api/Services/Telegram/TelegramActor.cs`
- Create: `src/Mindflow/Mindflow.Api/Services/Telegram/TelegramTaskCapture.cs`
- Modify: `src/Mindflow/Mindflow.Api/Services/Telegram/TelegramService.cs`
- Modify: `src/Mindflow/Mindflow.Api/Extensions/ServiceCollectionExtensions.cs` (`AddMindflowTelegram`)
- Create: `src/Mindflow/Mindflow.Api.Tests/Infrastructure/TelegramAiApiFactory.cs`
- Test: `src/Mindflow/Mindflow.Api.Tests/Telegram/TelegramTaskCaptureApiTests.cs`

**Interfaces:**
- Consumes: `IDictationService.ProcessAsync(DictationInput input, CancellationToken ct) : Task<DictationResponse>` (rzuca `TooManyRequestsException` przy limicie, inne `ApiException` przy błędach AI), `DictatedTaskResponse(Title, Description, DueDate, DueTime, Priority, ProjectId, ProjectName, Subtasks)`, `TelegramApi.SendMessageAsync`, `TelegramMessage` z Task 1.
- Produces:

```csharp
public enum TelegramTaskDraftStatus { Pending, Added, Cancelled }

public class TelegramTaskDraft
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Tasks { get; set; } = "[]";
    public TelegramTaskDraftStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
```

```csharp
public static class TelegramActor
{
    public static void ActAs(IHttpContextAccessor accessor, Guid userId);
}

public interface ITelegramTaskCapture
{
    Task HandleMessageAsync(Guid userId, TelegramMessage message, CancellationToken ct);
}
```

`TelegramTaskCapture.DraftJson` (`JsonSerializerDefaults.Web` + `JsonStringEnumConverter`) to format kolumny `tasks`: lista `DictatedTaskResponse` w camelCase z priorytetem jako tekst. `TelegramActor` jest też używany przez plan `telegram-ai-plan`.

- [ ] **Step 1: Fabryka testów AI + Telegram**

`src/Mindflow/Mindflow.Api.Tests/Infrastructure/TelegramAiApiFactory.cs`:

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Mindflow.Api.Services.Telegram;

namespace Mindflow.Api.Tests.Infrastructure;

public class TelegramAiApiFactory : AiApiFactory
{
    public FakeTelegramHandler Telegram { get; } = new();

    protected override IReadOnlyDictionary<string, string> Settings => new Dictionary<string, string>(base.Settings)
    {
        ["Telegram:BotToken"] = "123:abc",
        ["Telegram:BotUsername"] = "mindflow_test_bot",
        ["Telegram:WebhookSecret"] = "hook-secret",
        ["Notifications:FrontendUrl"] = "https://app.test"
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
            services.AddHttpClient<TelegramApi>().ConfigurePrimaryHttpMessageHandler(() => Telegram));
    }
}
```

`AiApiFactory` daje serwerowy klucz Gemini, fałszywy `Ai` i dzienny limit dyktowania 3.

- [ ] **Step 2: Write the failing test**

`src/Mindflow/Mindflow.Api.Tests/Telegram/TelegramTaskCaptureApiTests.cs`:

```csharp
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
}
```

Teksty wysyłane do AI są ASCII, bo ciało żądania Gemini ma znaki spoza ASCII zakodowane jako `\uXXXX` (patrz `DictationApiTests`).

- [ ] **Step 3: Run test to verify it fails**

Run: `ConnectionStrings__Database='…' dotnet test src/Mindflow/Mindflow.sln --filter TelegramTaskCaptureApiTests`
Expected: błąd kompilacji (`TelegramTaskDrafts`, `TelegramTaskDraftStatus` nie istnieją).

- [ ] **Step 4: Model, `DbContext` i migracja**

`src/Mindflow/Mindflow.Api/Models/Enums/TelegramTaskDraftStatus.cs`:

```csharp
namespace Mindflow.Api.Models.Enums;

public enum TelegramTaskDraftStatus
{
    Pending,
    Added,
    Cancelled
}
```

`src/Mindflow/Mindflow.Api/Models/TelegramTaskDraft.cs`:

```csharp
using Mindflow.Api.Models.Enums;

namespace Mindflow.Api.Models;

/// <summary>Tasks recognized from a Telegram message, waiting for the user's decision.</summary>
public class TelegramTaskDraft
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Tasks { get; set; } = "[]";
    public TelegramTaskDraftStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
```

`MindflowDbContext`: pod `public DbSet<TelegramConnection> TelegramConnections { get; set; }` dopisz

```csharp
    public DbSet<TelegramTaskDraft> TelegramTaskDrafts { get; set; }
```

i w `OnModelCreating`, zaraz po bloku `modelBuilder.Entity<TelegramConnection>(…)`:

```csharp
        modelBuilder.Entity<TelegramTaskDraft>(entity =>
        {
            entity.ToTable("telegram_task_drafts");
            entity.Property(d => d.Tasks).HasColumnType("jsonb");
            entity.Property(d => d.Status).HasConversion<string>().HasMaxLength(16);
            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(d => d.UserId);
        });
```

Migracja (z katalogu worktree):

```bash
dotnet ef migrations add AddTelegramTaskDrafts \
  --project src/Mindflow/Mindflow.Api/Mindflow.Api.csproj \
  --startup-project src/Mindflow/Mindflow.Api/Mindflow.Api.csproj
```

Sprawdź, że `Up` zawiera **tylko**: `CreateTable("telegram_task_drafts")` z kolumnami `id uuid NOT NULL`, `user_id uuid NOT NULL`, `tasks jsonb NOT NULL`, `status character varying(16) NOT NULL`, `created_at timestamp with time zone NOT NULL`, `expires_at timestamp with time zone NOT NULL`, kluczem `pk_telegram_task_drafts`, FK `fk_telegram_task_drafts_users_user_id` (`onDelete: ReferentialAction.Cascade`) oraz `CreateIndex("ix_telegram_task_drafts_user_id")`. `Down` usuwa tylko tę tabelę. Jeśli EF dorzuca cokolwiek innego (rozjazd snapshotu), zatrzymaj się i zgłoś. Potem:

```bash
ConnectionStrings__Database='Host=localhost;Port=5435;Database=mindflow_reminders_tests;Username=postgres;Password=postgres' \
  dotnet ef database update \
  --project src/Mindflow/Mindflow.Api/Mindflow.Api.csproj \
  --startup-project src/Mindflow/Mindflow.Api/Mindflow.Api.csproj
```

- [ ] **Step 5: `TelegramActor` i `TelegramTaskCapture`**

`src/Mindflow/Mindflow.Api/Services/Telegram/TelegramActor.cs`:

```csharp
using System.Security.Claims;

namespace Mindflow.Api.Services.Telegram;

public static class TelegramActor
{
    public static void ActAs(IHttpContextAccessor accessor, Guid userId)
    {
        var context = accessor.HttpContext ??= new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Telegram"));
    }
}
```

`src/Mindflow/Mindflow.Api/Services/Telegram/TelegramTaskCapture.cs`:

```csharp
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Data;
using Mindflow.Api.Exceptions;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Services.Ai.Dictation;

namespace Mindflow.Api.Services.Telegram;

public interface ITelegramTaskCapture
{
    Task HandleMessageAsync(Guid userId, TelegramMessage message, CancellationToken ct);
}

public class TelegramTaskCapture(
    MindflowDbContext db,
    TelegramApi api,
    IDictationService dictation,
    IHttpContextAccessor httpContextAccessor,
    ILogger<TelegramTaskCapture> logger) : ITelegramTaskCapture
{
    public const string NoTasksText = "Nie znalazłem zadań w tej wiadomości.";
    public const string LimitText = "Wykorzystano dzienny limit AI. Spróbuj jutro.";
    public const string AiErrorText = "Nie udało się teraz rozpoznać zadań. Spróbuj za chwilę.";

    private const int MaxListedTasks = 15;
    private const int MaxListedTitle = 80;
    private static readonly TimeSpan DraftLifetime = TimeSpan.FromHours(24);

    public static readonly JsonSerializerOptions DraftJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task HandleMessageAsync(Guid userId, TelegramMessage message, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(message.Text)) return;
        await CaptureAsync(userId, message.Chat.Id, new DictationInput(message.Text, null, null), ct);
    }

    private async Task CaptureAsync(Guid userId, long chatId, DictationInput input, CancellationToken ct)
    {
        DictationResponse result;
        try
        {
            TelegramActor.ActAs(httpContextAccessor, userId);
            result = await dictation.ProcessAsync(input, ct);
        }
        catch (TooManyRequestsException)
        {
            await api.SendMessageAsync(chatId, LimitText, null, ct);
            return;
        }
        catch (ApiException ex)
        {
            logger.LogWarning("Telegram dictation failed with status {Status}", ex.StatusCode);
            await api.SendMessageAsync(chatId, AiErrorText, null, ct);
            return;
        }

        if (result.Tasks.Count == 0)
        {
            await api.SendMessageAsync(chatId, NoTasksText, null, ct);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        await db.TelegramTaskDrafts.Where(d => d.UserId == userId && d.ExpiresAt < now).ExecuteDeleteAsync(ct);
        var draft = new TelegramTaskDraft
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Tasks = JsonSerializer.Serialize(result.Tasks, DraftJson),
            Status = TelegramTaskDraftStatus.Pending,
            CreatedAt = now,
            ExpiresAt = now.Add(DraftLifetime)
        };
        db.TelegramTaskDrafts.Add(draft);
        await db.SaveChangesAsync(ct);

        await api.SendMessageAsync(chatId, FormatDraft(result.Tasks),
        [[
            new TelegramButton("Dodaj wszystkie", CallbackData: $"cap:add:{draft.Id:N}"),
            new TelegramButton("Anuluj", CallbackData: $"cap:no:{draft.Id:N}")
        ]], ct);
    }

    public static string FormatDraft(IReadOnlyList<DictatedTaskResponse> tasks)
    {
        var lines = new List<string> { $"<b>📝 Rozpoznane zadania ({tasks.Count})</b>" };
        var number = 0;
        foreach (var task in tasks.Take(MaxListedTasks))
        {
            lines.Add($"{++number}. <b>{WebUtility.HtmlEncode(Truncate(task.Title, MaxListedTitle))}</b>");
            lines.Add(WebUtility.HtmlEncode(Details(task)));
        }

        if (tasks.Count > MaxListedTasks)
            lines.Add($"… i jeszcze {tasks.Count - MaxListedTasks}");
        return string.Join('\n', lines);
    }

    private static string Details(DictatedTaskResponse task)
    {
        var due = task.DueDate is { } date
            ? $"📅 {date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}{(task.DueTime is { } time ? " " + time.ToString("HH:mm", CultureInfo.InvariantCulture) : "")}"
            : "📅 bez terminu";
        return $"{due} · 📁 {task.ProjectName ?? "bez projektu"} · {task.Priority}";
    }

    private static string Truncate(string value, int max)
    {
        if (value.Length <= max) return value;
        var cut = char.IsHighSurrogate(value[max - 2]) ? max - 2 : max - 1;
        return $"{value[..cut]}…";
    }
}
```

- [ ] **Step 6: Kierowanie wiadomości w `TelegramService` i rejestracja**

`TelegramService` — nowy parametr konstruktora po `IReminderActionService reminderActions,`:

```csharp
public class TelegramService(
    MindflowDbContext db,
    TelegramApi api,
    IReminderActionService reminderActions,
    ITelegramTaskCapture capture,
    IConfiguration configuration) : ITelegramService
```

W `HandleUpdateAsync` zamień

```csharp
        var message = update.Message;
        if (message?.Text is not { } text) return;
```

na

```csharp
        var message = update.Message;
        if (message is null) return;
        var text = message.Text?.Trim() ?? "";
        if (!text.StartsWith('/') && (text.Length > 0 || message.Voice is not null))
        {
            await HandleNoteAsync(message, ct);
            return;
        }

        if (text.Length == 0) return;
```

(reszta metody — komendy `/start`, `/stop`, pomoc — bez zmian) i dopisz metodę:

```csharp
    private async Task HandleNoteAsync(TelegramMessage message, CancellationToken ct)
    {
        var userId = message.Chat.Type == PrivateChat
            ? await db.TelegramConnections.AsNoTracking()
                .Where(c => c.ChatId == message.Chat.Id)
                .Select(c => (Guid?)c.UserId)
                .FirstOrDefaultAsync(ct)
            : null;

        if (userId is null)
            await api.SendMessageAsync(message.Chat.Id, HelpText, null, ct);
        else
            await capture.HandleMessageAsync(userId.Value, message, ct);
    }
```

`ServiceCollectionExtensions.AddMindflowTelegram`, przed `services.AddScoped<ITelegramService, TelegramService>();`:

```csharp
        services.AddScoped<ITelegramTaskCapture, TelegramTaskCapture>();
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `ConnectionStrings__Database='…' dotnet test src/Mindflow/Mindflow.sln`
Expected: PASS (5 nowych + wszystkie dotychczasowe, w tym `TelegramLinkApiTests` i `TelegramButtonsApiTests` — komendy i przyciski działają jak wcześniej).

- [ ] **Step 8: Commit**

```bash
git add src/Mindflow/Mindflow.Api/Models/TelegramTaskDraft.cs \
  src/Mindflow/Mindflow.Api/Models/Enums/TelegramTaskDraftStatus.cs \
  src/Mindflow/Mindflow.Api/Data/MindflowDbContext.cs \
  src/Mindflow/Mindflow.Api/Migrations \
  src/Mindflow/Mindflow.Api/Services/Telegram/TelegramActor.cs \
  src/Mindflow/Mindflow.Api/Services/Telegram/TelegramTaskCapture.cs \
  src/Mindflow/Mindflow.Api/Services/Telegram/TelegramService.cs \
  src/Mindflow/Mindflow.Api/Extensions/ServiceCollectionExtensions.cs \
  src/Mindflow/Mindflow.Api.Tests/Infrastructure/TelegramAiApiFactory.cs \
  src/Mindflow/Mindflow.Api.Tests/Telegram/TelegramTaskCaptureApiTests.cs
git commit -m "feat: turn telegram messages into task drafts"
```

---

### Task 3: Głosówki

**Files:**
- Modify: `src/Mindflow/Mindflow.Api/Services/Telegram/TelegramTaskCapture.cs`
- Test: `src/Mindflow/Mindflow.Api.Tests/Telegram/TelegramTaskCaptureApiTests.cs`

**Interfaces:**
- Consumes: `TelegramApi.GetFileAsync`, `TelegramApi.DownloadFileAsync`, `TelegramVoice` (Task 1), `FakeTelegramHandler.Files` (Task 1), `AiOptions.Dictation.MaxAudioBytes`, `AudioFormats.Normalize(string?) : string?`.
- Produces: `TelegramTaskCapture.HandleMessageAsync` obsługuje `message.Voice` (sygnatura bez zmian).

- [ ] **Step 1: Write the failing tests** (dopisz do `TelegramTaskCaptureApiTests`)

```csharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `ConnectionStrings__Database='…' dotnet test src/Mindflow/Mindflow.sln --filter TelegramTaskCaptureApiTests`
Expected: FAIL — 3 nowe testy (głosówka jest dziś ignorowana, więc brak odpowiedzi i brak wywołania AI).

- [ ] **Step 3: Write minimal implementation**

W `TelegramTaskCapture.cs`:

1. Dodatkowe `using`:

```csharp
using Microsoft.Extensions.Options;
using Mindflow.Api.Services.Ai;
using Mindflow.Api.Services.Ai.Providers;
```

2. Nowy parametr konstruktora po `IDictationService dictation,`:

```csharp
    IOptions<AiOptions> aiOptions,
```

3. Nowe stałe (obok pozostałych):

```csharp
    public const string VoiceTooLongText = "Głosówka może mieć najwyżej 3 minuty.";
    public const string DownloadFailedText = "Nie udało się pobrać głosówki. Spróbuj jeszcze raz.";

    private const int MaxVoiceSeconds = 180;
    private const string DefaultVoiceMime = "audio/ogg";
```

4. Zamień `HandleMessageAsync` i dopisz `ReadVoiceAsync`:

```csharp
    public async Task HandleMessageAsync(Guid userId, TelegramMessage message, CancellationToken ct)
    {
        var input = message.Voice is { } voice
            ? await ReadVoiceAsync(message.Chat.Id, voice, ct)
            : string.IsNullOrWhiteSpace(message.Text) ? null : new DictationInput(message.Text, null, null);
        if (input is not null) await CaptureAsync(userId, message.Chat.Id, input, ct);
    }

    private async Task<DictationInput?> ReadVoiceAsync(long chatId, TelegramVoice voice, CancellationToken ct)
    {
        var maxBytes = aiOptions.Value.Dictation.MaxAudioBytes;
        if (voice.Duration > MaxVoiceSeconds || voice.FileSize > maxBytes)
        {
            await api.SendMessageAsync(chatId, VoiceTooLongText, null, ct);
            return null;
        }

        var file = await api.GetFileAsync(voice.FileId, ct);
        var audio = file?.FilePath is { } path ? await api.DownloadFileAsync(path, maxBytes, ct) : null;
        if (audio is not { Length: > 0 })
        {
            await api.SendMessageAsync(chatId, DownloadFailedText, null, ct);
            return null;
        }

        return new DictationInput(null, audio, AudioFormats.Normalize(voice.MimeType) ?? DefaultVoiceMime);
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `ConnectionStrings__Database='…' dotnet test src/Mindflow/Mindflow.sln --filter "FullyQualifiedName~Telegram"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Mindflow/Mindflow.Api/Services/Telegram/TelegramTaskCapture.cs \
  src/Mindflow/Mindflow.Api.Tests/Telegram/TelegramTaskCaptureApiTests.cs
git commit -m "feat: accept voice messages in telegram"
```

---

### Task 4: Przyciski `Dodaj wszystkie` / `Anuluj`

**Files:**
- Modify: `src/Mindflow/Mindflow.Api/Services/Telegram/TelegramTaskCapture.cs`
- Modify: `src/Mindflow/Mindflow.Api/Services/Telegram/TelegramService.cs` (`HandleCallbackAsync`)
- Test: `src/Mindflow/Mindflow.Api.Tests/Telegram/TelegramDraftButtonsApiTests.cs`

**Interfaces:**
- Consumes: `ITaskService.CreateAsync(CreateTaskRequest request, TaskExternalReference? externalReference = null) : Task<TaskDetailResponse?>`, `CreateTaskRequest(Content, Description, Priority, Status, DueDate, DueTime, EstimatedHours, ProjectId, Tags, Subtasks, …)`, `TaskSubtaskRequest(Id, Content, IsCompleted, Status, EstimatedHours, ClearEstimatedHours, Description, DueDate, SortOrder)`, `TelegramApi.AnswerCallbackQueryAsync`, `TelegramApi.RemoveKeyboardAsync` (Task 1), `TelegramActor.ActAs`, `TelegramTaskCapture.DraftJson` (Task 2).
- Produces:

```csharp
public interface ITelegramTaskCapture
{
    Task HandleMessageAsync(Guid userId, TelegramMessage message, CancellationToken ct);
    Task HandleCallbackAsync(Guid userId, TelegramCallbackQuery callback, CancellationToken ct);
}
```

`TelegramService.HandleCallbackAsync` po ustaleniu właściciela czatu przekazuje dane zaczynające się od `cap:` do `capture.HandleCallbackAsync(row.UserId, …)`. Plan `telegram-ai-plan` dopisze obok analogiczną gałąź dla `sg:`.

- [ ] **Step 1: Write the failing test**

`src/Mindflow/Mindflow.Api.Tests/Telegram/TelegramDraftButtonsApiTests.cs`:

```csharp
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
```

`MindflowApiFactory.Json` (`Web` + `JsonStringEnumConverter`) daje ten sam kształt JSON-a co `TelegramTaskCapture.DraftJson`.

- [ ] **Step 2: Run test to verify it fails**

Run: `ConnectionStrings__Database='…' dotnet test src/Mindflow/Mindflow.sln --filter TelegramDraftButtonsApiTests`
Expected: FAIL — dziś `cap:…` trafia do parsera przypomnień i dostaje `Nie znaleziono zadania`.

- [ ] **Step 3: Write minimal implementation**

`TelegramTaskCapture.cs`:

1. Interfejs:

```csharp
public interface ITelegramTaskCapture
{
    Task HandleMessageAsync(Guid userId, TelegramMessage message, CancellationToken ct);
    Task HandleCallbackAsync(Guid userId, TelegramCallbackQuery callback, CancellationToken ct);
}
```

2. Nowy parametr konstruktora po `IOptions<AiOptions> aiOptions,`:

```csharp
    ITaskService taskService,
```

3. Nowe stałe:

```csharp
    public const string CancelledText = "Anulowano";
    public const string HandledDraftText = "Ta lista jest już obsłużona.";
    public const string ExpiredDraftText = "Ta lista wygasła. Wyślij wiadomość jeszcze raz.";
    public const string NothingAddedText = "Nie udało się dodać zadań.";
```

4. Nowe metody:

```csharp
    public async Task HandleCallbackAsync(Guid userId, TelegramCallbackQuery callback, CancellationToken ct)
    {
        var parts = callback.Data?.Split(':');
        if (parts is not ["cap", var verb, var id] || verb is not ("add" or "no") || !Guid.TryParseExact(id, "N", out var draftId))
        {
            await api.AnswerCallbackQueryAsync(callback.Id, ExpiredDraftText, ct);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var target = verb == "add" ? TelegramTaskDraftStatus.Added : TelegramTaskDraftStatus.Cancelled;
        var claimed = await db.TelegramTaskDrafts
            .Where(d => d.Id == draftId && d.UserId == userId && d.Status == TelegramTaskDraftStatus.Pending && d.ExpiresAt > now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(d => d.Status, target), ct);
        if (claimed == 0)
        {
            var handled = await db.TelegramTaskDrafts.AnyAsync(
                d => d.Id == draftId && d.UserId == userId && d.Status != TelegramTaskDraftStatus.Pending, ct);
            await api.AnswerCallbackQueryAsync(callback.Id, handled ? HandledDraftText : ExpiredDraftText, ct);
            return;
        }

        var answer = verb == "add" ? AddedText(await AddTasksAsync(userId, draftId, ct)) : CancelledText;
        await api.AnswerCallbackQueryAsync(callback.Id, answer, ct);
        if (callback.Message is { } message)
            await api.RemoveKeyboardAsync(message.Chat.Id, message.MessageId, ct);
    }

    public static string AddedText(int count) =>
        count == 0 ? NothingAddedText : $"✅ Dodano {count} {TaskWord(count)}";

    private static string TaskWord(int count) =>
        count == 1 ? "zadanie"
        : count % 10 is >= 2 and <= 4 && count % 100 is < 12 or > 14 ? "zadania"
        : "zadań";

    private async Task<int> AddTasksAsync(Guid userId, Guid draftId, CancellationToken ct)
    {
        var json = await db.TelegramTaskDrafts.AsNoTracking()
            .Where(d => d.Id == draftId)
            .Select(d => d.Tasks)
            .SingleAsync(ct);
        var tasks = JsonSerializer.Deserialize<List<DictatedTaskResponse>>(json, DraftJson) ?? [];

        TelegramActor.ActAs(httpContextAccessor, userId);
        var added = 0;
        foreach (var task in tasks)
        {
            try
            {
                if (await taskService.CreateAsync(ToRequest(task)) is not null) added++;
            }
            catch (Exception ex) when (ex is ApiException or DbUpdateException)
            {
                logger.LogWarning("Telegram draft task was not created: {ExceptionType}", ex.GetType().Name);
            }
        }

        return added;
    }

    private static CreateTaskRequest ToRequest(DictatedTaskResponse task) => new(
        task.Title,
        task.Description,
        task.Priority,
        null,
        task.DueDate,
        task.DueDate is null ? null : task.DueTime,
        null,
        task.ProjectId,
        null,
        task.Subtasks
            .Select((content, index) => new TaskSubtaskRequest(null, content, false, null, null, false, null, null, index))
            .ToList());
```

`TelegramService.HandleCallbackAsync` — zaraz po bloku `if (row is null) { … NotLinkedText … return; }` dopisz:

```csharp
        if (callback.Data?.StartsWith("cap:", StringComparison.Ordinal) == true)
        {
            await capture.HandleCallbackAsync(row.UserId, callback, ct);
            return;
        }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `ConnectionStrings__Database='…' dotnet test src/Mindflow/Mindflow.sln`
Expected: PASS (4 nowe + wszystkie dotychczasowe, w tym `TelegramButtonsApiTests` dla `done:`/`snooze:`).

- [ ] **Step 5: Commit**

```bash
git add src/Mindflow/Mindflow.Api/Services/Telegram/TelegramTaskCapture.cs \
  src/Mindflow/Mindflow.Api/Services/Telegram/TelegramService.cs \
  src/Mindflow/Mindflow.Api.Tests/Telegram/TelegramDraftButtonsApiTests.cs
git commit -m "feat: add or cancel telegram task drafts"
```

---

### Task 5: Dokumentacja

**Files:**
- Modify: `docs/push-notifications.md` (sekcja `## Telegram`)

- [ ] **Step 1: Dopisz podsekcję przed „Jednorazowe ustawienie webhooka na produkcji”**

```markdown
### Dodawanie zadań z bota

Wiadomość tekstowa (nie komenda) albo głosówka wysłana do bota z połączonego prywatnego czatu trafia do tego samego silnika co `POST /ai/dictation`: ta sama konfiguracja AI użytkownika (Ustawienia → AI) i ten sam dzienny limit dyktowania. Głosówka może mieć najwyżej 3 minuty i `Ai:Dictation:MaxAudioBytes` (domyślnie 8 MB). Bot pobiera ją przez `getFile` i `https://api.telegram.org/file/bot<token>/<path>`; ani token, ani ścieżka pliku nie trafiają do logów.

Bot odpisuje listą rozpoznanych zadań (tytuł, termin, projekt, priorytet) z przyciskami `Dodaj wszystkie` i `Anuluj`. Lista czeka w tabeli `telegram_task_drafts` przez 24 godziny. „Dodaj wszystkie” tworzy zadania tak jak zwykłe dodawanie (aktywność, odświeżenie na żywo w aplikacji), a drugie kliknięcie niczego nie dubluje. Po kliknięciu bot zdejmuje przyciski. Wiadomość z niepołączonego czatu dostaje instrukcję połączenia.

Uwaga: przy webhooku rozpoznanie AI dzieje się w trakcie żądania od Telegrama (do `Ai:Dictation:TimeoutSeconds`, domyślnie 60 s). Gdy Telegram uzna, że webhook nie odpowiedział, może wysłać tę samą wiadomość ponownie i bot pokaże drugą listę; wystarczy ją anulować.

Migracja `AddTelegramTaskDrafts` zastosowana jak pozostałe (punkt 1). Wdrożenie: VPS (`deploy/vps/deploy.sh ../mindflow-api-reminders`), migracje skryptem idempotentnym z backupem bazy.
```

- [ ] **Step 2: Commit**

```bash
git add docs/push-notifications.md
git commit -m "docs: describe adding tasks from telegram"
```

---

## Pokrycie specu (sekcja 2)

| Wymaganie | Zadanie |
|---|---|
| Tekst (nie komenda) albo głosówka z połączonego prywatnego czatu → silnik dyktowania z limitem i konfiguracją AI użytkownika | Task 2 (`HandleNoteAsync`, `CaptureAsync`), Task 3 |
| Głosówka ogg/opus, maks. 3 min, `getFile` + pobranie, token nie w logach | Task 1 (`GetFileAsync`, `DownloadFileAsync`, test logów), Task 3 |
| Lista: tytuł, termin (data i godzina), projekt, priorytet + `Dodaj wszystkie` / `Anuluj` | Task 2 (`FormatDraft`), test `Text_message_becomes_a_draft_with_buttons` |
| Tabela `telegram_task_drafts` (id, user, JSON, utworzono, wygasa po 24 h, status), `cap:add:<id N>` / `cap:no:<id N>` | Task 2 (model, migracja) |
| „Dodaj wszystkie” przez `TaskService.CreateAsync` w imieniu użytkownika, bez dublowania, „✅ Dodano N zadań”, `editMessageReplyMarkup` | Task 4, test `Add_all_creates_the_tasks_once` |
| „Anuluj” → „Anulowano” | Task 4, test `Cancel_creates_nothing` |
| Szkic innego użytkownika odrzucony | Task 4, test `Draft_of_another_user_is_rejected` |
| Brak zadań / limit / błąd AI — krótkie komunikaty po polsku | Task 2, testy `Message_without_tasks_…`, `Daily_limit_…`, `Ai_error_…` |
| Niepołączony czat → instrukcja połączenia | Task 2, test `Unlinked_or_group_chat_gets_connect_instructions` |
