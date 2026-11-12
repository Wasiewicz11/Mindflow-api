# Reminder Telegram (API, etap 4) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use subagent-driven-development (recommended) or executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Użytkownik łączy konto z botem Telegrama (link `t.me/<bot>?start=<kod>`), a przypomnienia o zadaniach przychodzą od bota z przyciskami Zrobione / Odłóż 10 min / Otwórz.

**Architecture:** Własny cienki klient Bot API (`TelegramApi`, typowany `HttpClient`, bez biblioteki). `TelegramService` obsługuje łączenie i aktualizacje (`/start <kod>`, `/stop`, przyciski) niezależnie od transportu. Na produkcji aktualizacje przychodzą webhookiem (`POST /integrations/telegram/webhook`), a lokalnie przez `TelegramPollingService` (`getUpdates`). `TelegramReminderChannel` to kolejny `IReminderChannel`. Przyciski wołają `IReminderActionService` z etapu 1.

**Tech Stack:** .NET 10, `HttpClient` + `System.Text.Json` (`JsonNamingPolicy.SnakeCaseLower`), EF Core, `BackgroundService`, xUnit + WebApplicationFactory.

**Spec:** `/Users/filipwasiewicz/Projekty/Mindflow/mindflow-web-reminders/docs/superpowers/specs/2026-11-09-przypomnienia-design.md`

## Global Constraints

- Worktree `/Users/filipwasiewicz/Projekty/Mindflow/mindflow-api-reminders`, gałąź `feature/reminder-telegram` od `feature/reminder-email`.
- Commity: Conventional Commits po angielsku, małą literą, tryb rozkazujący, bez opisu, bez `Co-Authored-By`, bez wzmianek o AI. Test w tym samym commicie co kod. Zero komentarzy „dlaczego”.
- Konfiguracja: `Telegram:BotToken`, `Telegram:BotUsername` (bez `@`), `Telegram:WebhookSecret`, `Telegram:UsePolling` (`true` tylko lokalnie). Bez tokenu i nazwy bota funkcja jest wyłączona: status `configured=false`, `POST link` zwraca 400, a kanał nic nie wysyła.
- Kod łączący: 16 losowych bajtów w base64url (22 znaki), ważny 15 min, w bazie tylko hash SHA-256 (hex). Jeden czat = jedno konto: połączenie czatu z nowym kontem odłącza go od poprzedniego.
- Dane przycisków: `done:{taskId:N}` i `snooze:{taskId:N}` (maks. 64 bajty). Przycisk działa tylko dla zadania właściciela połączonego czatu.
- Webhook: nagłówek `X-Telegram-Bot-Api-Secret-Token` porównywany w stałym czasie z `Telegram:WebhookSecret`. Zły albo brakujący sekret (także gdy sekret nie jest skonfigurowany) daje 401. Poprawne żądanie zawsze dostaje 200, a błędy obsługi idą tylko do logów.
- Wiadomość przypomnienia (HTML, każdy tekst przez `WebUtility.HtmlEncode`): `<b>⏰ Przypomnienie</b>\n<b>{tytuł}</b>\n{treść}`. Klawiatura: wiersz `[✅ Zrobione][⏰ Odłóż 10 min]` i, tylko gdy `TaskUrl` zaczyna się od `https://`, wiersz `[Otwórz zadanie → url]`.
- Teksty bota po polsku (dokładne brzmienie w zadaniach).
- `NotificationSettings.TelegramRemindersEnabled = true` (domyślnie). `PUT /notifications/settings` dostaje opcjonalne `telegramRemindersEnabled` (`null` = bez zmian), a odpowiedź zawiera `telegramRemindersEnabled`.
- Testy DB: `ConnectionStrings__Database='Host=localhost;Port=5435;Database=mindflow_reminders_tests;Username=postgres;Password=postgres'`. Po migracji uruchom `dotnet ef database update`.

---

### Task 1: Model, ustawienie i klient Bot API

**Files:**
- Create: `src/Mindflow/Mindflow.Api/Models/TelegramConnection.cs`
- Modify: `src/Mindflow/Mindflow.Api/Data/MindflowDbContext.cs` (`DbSet<TelegramConnection> TelegramConnections`, tabela `telegram_connections`, FK do `User` z kaskadą, unikalny indeks na `ChatId` z filtrem `"chat_id IS NOT NULL"`, indeks na `LinkCodeHash`)
- Modify: `src/Mindflow/Mindflow.Api/Models/NotificationSettings.cs` (`public bool TelegramRemindersEnabled { get; set; } = true;`), `Models/Dtos/NotificationDtos.cs`, `Services/NotificationService.cs` (jak `EmailRemindersEnabled` z etapu 3: odpowiedź + opcjonalne pole w PUT, dopisane na końcu rekordów)
- Create (generated): migracja `AddTelegramReminders` (tabela + kolumna `telegram_reminders_enabled boolean NOT NULL DEFAULT true`; sprawdź `defaultValue: true`)
- Create: `src/Mindflow/Mindflow.Api/Services/Telegram/TelegramOptions.cs`, `TelegramModels.cs`, `TelegramApi.cs`
- Modify: `src/Mindflow/Mindflow.Api/Extensions/ServiceCollectionExtensions.cs` (`services.AddHttpClient<TelegramApi>(c => { c.BaseAddress = new Uri("https://api.telegram.org/"); c.Timeout = TimeSpan.FromSeconds(40); });`)
- Test: `src/Mindflow/Mindflow.Api.Tests/Infrastructure/FakeTelegramHandler.cs`, `src/Mindflow/Mindflow.Api.Tests/Telegram/TelegramApiTests.cs`

**Interfaces:**
- Produces:

```csharp
public class TelegramConnection
{
    public Guid UserId { get; set; }
    public long? ChatId { get; set; }
    [MaxLength(64)] public string? Username { get; set; }
    [MaxLength(64)] public string? LinkCodeHash { get; set; }
    public DateTimeOffset? LinkCodeExpiresAt { get; set; }
    public DateTimeOffset? ConnectedAt { get; set; }
}
```

```csharp
namespace Mindflow.Api.Services.Telegram;

public record TelegramOptions(string BotToken, string BotUsername, string? WebhookSecret, bool UsePolling)
{
    public static TelegramOptions? From(IConfiguration configuration)
    {
        var token = configuration["Telegram:BotToken"];
        var username = configuration["Telegram:BotUsername"]?.TrimStart('@');
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(username)) return null;
        return new TelegramOptions(token, username, configuration["Telegram:WebhookSecret"],
            bool.TryParse(configuration["Telegram:UsePolling"], out var polling) && polling);
    }
}

public record TelegramUpdate(long UpdateId, TelegramMessage? Message, TelegramCallbackQuery? CallbackQuery);
public record TelegramMessage(long MessageId, TelegramChat Chat, TelegramUser? From, string? Text);
public record TelegramChat(long Id);
public record TelegramUser(long Id, string? Username, string? FirstName);
public record TelegramCallbackQuery(string Id, TelegramUser From, TelegramMessage? Message, string? Data);
public record TelegramButton(string Text, string? CallbackData = null, string? Url = null);

public static class TelegramJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
```

```csharp
public class TelegramApi(HttpClient http, IConfiguration configuration, ILogger<TelegramApi> logger)
{
    public Task<bool> SendMessageAsync(long chatId, string html, IReadOnlyList<IReadOnlyList<TelegramButton>>? keyboard, CancellationToken ct);
    public Task<bool> AnswerCallbackQueryAsync(string callbackQueryId, string text, CancellationToken ct);
    public Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(long offset, int timeoutSeconds, CancellationToken ct);
    public Task<bool> DeleteWebhookAsync(CancellationToken ct);
}
```

Każda metoda robi `POST bot{token}/{method}` z JSON-em (`TelegramJson.Options`). `sendMessage` wysyła `{ chat_id, text, parse_mode: "HTML", disable_web_page_preview: true, reply_markup: { inline_keyboard } }` (bez `reply_markup`, gdy brak klawiatury), `answerCallbackQuery` wysyła `{ callback_query_id, text }`, `getUpdates` wysyła `{ offset, timeout, allowed_updates: ["message","callback_query"] }` i zwraca `result`, a `deleteWebhook` wysyła `{}`. Metoda zwraca `ok` z odpowiedzi. Gdy brak konfiguracji, `false` albo pusta lista bez wywołania. Odpowiedź inna niż 2xx daje ostrzeżenie w logu i `false`, **bez tokenu w logu**.

- [ ] **Step 1: Write the failing test** (`FakeTelegramHandler` + `TelegramApiTests`)

```csharp
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Mindflow.Api.Tests.Infrastructure;

public sealed class FakeTelegramHandler : HttpMessageHandler
{
    public List<(string Method, JsonObject Body)> Requests { get; } = [];
    public Queue<string> UpdatesResults { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var method = request.RequestUri!.AbsolutePath.Split('/').Last();
        var body = request.Content is null ? new JsonObject() : JsonNode.Parse(await request.Content.ReadAsStringAsync(ct))!.AsObject();
        lock (Requests) Requests.Add((method, body));
        var result = method == "getUpdates" ? (UpdatesResults.TryDequeue(out var next) ? next : "[]") : "true";
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($$"""{"ok":true,"result":{{result}}}""", Encoding.UTF8, "application/json") };
    }
}
```

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Mindflow.Api.Services.Telegram;
using Mindflow.Api.Tests.Infrastructure;

namespace Mindflow.Api.Tests.Telegram;

public class TelegramApiTests
{
    private static (TelegramApi Api, FakeTelegramHandler Handler) Create(bool configured = true)
    {
        var handler = new FakeTelegramHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.telegram.org/") };
        var values = configured
            ? new Dictionary<string, string?> { ["Telegram:BotToken"] = "123:abc", ["Telegram:BotUsername"] = "mindflow_test_bot" }
            : new Dictionary<string, string?>();
        return (new TelegramApi(http, new ConfigurationBuilder().AddInMemoryCollection(values).Build(), NullLogger<TelegramApi>.Instance), handler);
    }

    [Fact]
    public async Task Sends_html_message_with_inline_keyboard()
    {
        var (api, handler) = Create();

        var ok = await api.SendMessageAsync(42, "<b>Hi</b>", [[new TelegramButton("✅ Zrobione", CallbackData: "done:abc")]], CancellationToken.None);

        Assert.True(ok);
        var (method, body) = Assert.Single(handler.Requests);
        Assert.Equal("sendMessage", method);
        Assert.Equal(42, (long)body["chat_id"]!);
        Assert.Equal("HTML", (string)body["parse_mode"]!);
        Assert.Equal("done:abc", (string)body["reply_markup"]!["inline_keyboard"]![0]![0]!["callback_data"]!);
        Assert.Null(body["reply_markup"]!["inline_keyboard"]![0]![0]!["url"]);
    }

    [Fact]
    public async Task Parses_updates()
    {
        var (api, handler) = Create();
        handler.UpdatesResults.Enqueue("""[{"update_id":7,"message":{"message_id":1,"chat":{"id":42},"from":{"id":42,"username":"filip"},"text":"/start abc"}}]""");

        var updates = await api.GetUpdatesAsync(0, 0, CancellationToken.None);

        var update = Assert.Single(updates);
        Assert.Equal((7L, 42L, "/start abc"), (update.UpdateId, update.Message!.Chat.Id, update.Message.Text));
    }

    [Fact]
    public async Task Does_nothing_without_configuration()
    {
        var (api, handler) = Create(configured: false);
        Assert.False(await api.SendMessageAsync(1, "x", null, CancellationToken.None));
        Assert.Empty(handler.Requests);
    }
}
```

- [ ] **Step 2: Run** → błąd kompilacji.
- [ ] **Step 3: Implement** model, DbContext, ustawienie, migrację (`dotnet ef migrations add AddTelegramReminders …` + `database update`) i `TelegramApi`. W `SendMessageAsync` zmapuj `TelegramButton` na `{ text, callback_data | url }`. Dzięki `WhenWritingNull` pola `null` wypadają.
- [ ] **Step 4: Run** pełne testy z DB → zielone.
- [ ] **Step 5: Commit** `feat: add telegram bot client and connection model`

---

### Task 2: Łączenie konta (status, link, rozłączenie, `/start`, `/stop`, webhook)

**Files:**
- Create: `src/Mindflow/Mindflow.Api/Services/Telegram/TelegramService.cs` (`ITelegramService` + impl)
- Create: `src/Mindflow/Mindflow.Api/Controllers/TelegramController.cs`
- Modify: `src/Mindflow/Mindflow.Api/Models/Dtos/NotificationDtos.cs` (`record TelegramStatusResponse(bool Configured, bool Connected, string? Username)`, `record TelegramLinkResponse(string Url)`)
- Modify: `src/Mindflow/Mindflow.Api/Extensions/ServiceCollectionExtensions.cs` (`services.AddScoped<ITelegramService, TelegramService>();`)
- Test: `src/Mindflow/Mindflow.Api.Tests/Infrastructure/TelegramApiFactory.cs`, `src/Mindflow/Mindflow.Api.Tests/Telegram/TelegramLinkApiTests.cs`

**Interfaces:**
- Consumes: `TelegramApi`, `TelegramOptions`, `TelegramUpdate` (Task 1), `IReminderActionService.ApplyAsync(Guid userId, Guid taskId, ReminderAction action, CancellationToken ct) : Task<ReminderActionResponse?>` (etap 1; działa także bez żądania HTTP).
- Produces:

```csharp
public interface ITelegramService
{
    Task<TelegramStatusResponse> GetStatusAsync(Guid userId, CancellationToken ct);
    Task<TelegramLinkResponse> CreateLinkAsync(Guid userId, CancellationToken ct);
    Task DisconnectAsync(Guid userId, CancellationToken ct);
    Task HandleUpdateAsync(TelegramUpdate update, CancellationToken ct);
}
```

Endpointy (`[Route("integrations/telegram")]`, `[Authorize]`): `GET` → status, `POST link` → link (400 `{ message }` bez konfiguracji), `DELETE` → 204, `POST webhook` (`[AllowAnonymous]`) → 401 / 200.

Teksty bota (dokładnie):
- po połączeniu: `✅ Połączono z Mindflow. Tutaj będą przychodzić przypomnienia o zadaniach.`
- zły lub wygasły kod: `Ten link wygasł. Wygeneruj nowy w Mindflow: Ustawienia → Powiadomienia → Telegram.`
- `/start` bez kodu albo nieznana komenda: `Połącz konto w Mindflow: Ustawienia → Powiadomienia → Telegram.`
- `/stop`: `Odłączono. Nie będę już wysyłać przypomnień.`

- [ ] **Step 1: Write the failing test**

`TelegramApiFactory`: podklasa `MindflowApiFactory` z ustawieniami `Telegram:BotToken=123:abc`, `Telegram:BotUsername=mindflow_test_bot`, `Telegram:WebhookSecret=hook-secret` i w `ConfigureWebHost` `services.AddHttpClient<TelegramApi>().ConfigurePrimaryHttpMessageHandler(() => Telegram)`, gdzie `public FakeTelegramHandler Telegram { get; } = new();` (wzór: `AiApiFactory`).

```csharp
using System.Net;
using System.Net.Http.Json;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Tests.Infrastructure;

namespace Mindflow.Api.Tests.Telegram;

public class TelegramLinkApiTests(TelegramApiFactory factory) : IClassFixture<TelegramApiFactory>
{
    private static long chatSeed = Random.Shared.NextInt64(1_000_000, 9_000_000);

    [LocalDatabaseFact]
    public async Task Start_with_link_code_connects_the_chat()
    {
        var user = await factory.CreateUserAsync();
        var client = factory.CreateUserClient(user.Id);
        var chatId = Interlocked.Increment(ref chatSeed);

        var link = await (await client.PostAsync("/integrations/telegram/link", null)).Content.ReadFromJsonAsync<TelegramLinkResponse>(MindflowApiFactory.Json);
        Assert.StartsWith("https://t.me/mindflow_test_bot?start=", link!.Url);
        var code = link.Url.Split("start=")[1];

        Assert.Equal(HttpStatusCode.OK, (await Webhook(Start(chatId, $"/start {code}"))).StatusCode);

        var status = await client.GetFromJsonAsync<TelegramStatusResponse>("/integrations/telegram", MindflowApiFactory.Json);
        Assert.Equal((true, true, "filip"), (status!.Configured, status.Connected, status.Username));
        Assert.Contains(factory.Telegram.Requests, r => r.Method == "sendMessage" && (long)r.Body["chat_id"]! == chatId && ((string)r.Body["text"]!).StartsWith("✅ Połączono"));
    }

    [LocalDatabaseFact]
    public async Task Wrong_or_used_code_does_not_connect()
    {
        var user = await factory.CreateUserAsync();
        var client = factory.CreateUserClient(user.Id);
        var chatId = Interlocked.Increment(ref chatSeed);

        await Webhook(Start(chatId, "/start not-a-code"));

        var status = await client.GetFromJsonAsync<TelegramStatusResponse>("/integrations/telegram", MindflowApiFactory.Json);
        Assert.False(status!.Connected);
        Assert.Contains(factory.Telegram.Requests, r => (long?)r.Body["chat_id"] == chatId && ((string)r.Body["text"]!).StartsWith("Ten link wygasł"));
    }

    [LocalDatabaseFact]
    public async Task Stop_and_delete_disconnect()
    {
        var user = await factory.CreateUserAsync();
        var client = factory.CreateUserClient(user.Id);
        var chatId = Interlocked.Increment(ref chatSeed);
        var link = await (await client.PostAsync("/integrations/telegram/link", null)).Content.ReadFromJsonAsync<TelegramLinkResponse>(MindflowApiFactory.Json);
        await Webhook(Start(chatId, $"/start {link!.Url.Split("start=")[1]}"));

        await Webhook(Start(chatId, "/stop"));

        var status = await client.GetFromJsonAsync<TelegramStatusResponse>("/integrations/telegram", MindflowApiFactory.Json);
        Assert.False(status!.Connected);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/integrations/telegram")).StatusCode);
    }

    [LocalDatabaseFact]
    public async Task Webhook_rejects_wrong_secret()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/integrations/telegram/webhook") { Content = JsonContent.Create(Start(1, "/stop")) };
        request.Headers.Add("X-Telegram-Bot-Api-Secret-Token", "nope");
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().SendAsync(request)).StatusCode);
    }

    private static object Start(long chatId, string text) => new
    {
        update_id = 1,
        message = new { message_id = 1, chat = new { id = chatId }, from = new { id = chatId, username = "filip" }, text }
    };

    private Task<HttpResponseMessage> Webhook(object update)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/integrations/telegram/webhook") { Content = JsonContent.Create(update) };
        request.Headers.Add("X-Telegram-Bot-Api-Secret-Token", "hook-secret");
        return factory.CreateClient().SendAsync(request);
    }
}
```

Webhook deserializuje ciało przez `TelegramJson.Options` (snake_case), np. `JsonSerializer.DeserializeAsync<TelegramUpdate>(Request.Body, TelegramJson.Options)`, a nie przez domyślny binder MVC, który oczekuje camelCase.

- [ ] **Step 2: Run** → błąd kompilacji.
- [ ] **Step 3: Implement** `TelegramService`:
  - `GetStatusAsync`: `Configured = TelegramOptions.From(config) != null`, `Connected = row?.ChatId != null`.
  - `CreateLinkAsync`: bez konfiguracji `BadRequestException("Telegram is not configured on the server.")`. Kod `WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(16))`, upsert wiersza (`LinkCodeHash = Convert.ToHexString(SHA256.HashData(UTF8(code))).ToLowerInvariant()`, `LinkCodeExpiresAt = now + 15 min`), URL `https://t.me/{BotUsername}?start={code}`.
  - `DisconnectAsync`: usuń wiersz, jeśli istnieje.
  - `HandleUpdateAsync`: `Message.Text` `/start <kod>` → wiersz po hashu z `LinkCodeExpiresAt > now`. Jest: usuń `ChatId` z innych wierszy z tym czatem, ustaw `ChatId`, `Username = From.Username`, `ConnectedAt = now`, wyczyść kod, zapisz i wyślij tekst „Połączono”. Nie ma: tekst „Ten link wygasł…”. `/stop` → usuń wiersz z tym `ChatId` i wyślij tekst „Odłączono…”. Inny tekst → instrukcja. `CallbackQuery` zostaje na Task 3 (na razie ignoruj).
  - Kontroler: użytkownik przez `ICurrentUserService` (jak `NotificationsController`). Webhook: brak `WebhookSecret` w konfiguracji albo nagłówek niezgodny (`CryptographicOperations.FixedTimeEquals` na bajtach UTF-8) → 401. Potem deserializacja i `HandleUpdateAsync` w `try/catch` (log `LogWarning`) i zawsze `Ok()`.
- [ ] **Step 4: Run** pełne testy z DB → zielone.
- [ ] **Step 5: Commit** `feat: connect a telegram chat to the account`

---

### Task 3: Przyciski i polling

**Files:**
- Modify: `src/Mindflow/Mindflow.Api/Services/Telegram/TelegramService.cs` (obsługa `CallbackQuery`)
- Create: `src/Mindflow/Mindflow.Api/Services/Telegram/TelegramPollingService.cs`
- Modify: `src/Mindflow/Mindflow.Api/Extensions/ServiceCollectionExtensions.cs` (`if (TelegramOptions.From(config)?.UsePolling == true) services.AddHostedService<TelegramPollingService>();`; jeśli `AddMindflowServices` nie dostaje `IConfiguration`, zarejestruj w metodzie, która je ma, np. `AddMindflowIntegrations`)
- Test: `src/Mindflow/Mindflow.Api.Tests/Telegram/TelegramButtonsApiTests.cs`

**Interfaces:**
- Consumes: `IReminderActionService`, `ReminderAction` (etap 1), `TelegramApi.AnswerCallbackQueryAsync`, `TelegramApi.GetUpdatesAsync`, `TelegramApi.DeleteWebhookAsync`.

Odpowiedzi na przyciski (`answerCallbackQuery`, dokładnie): `✅ Ukończono`, `⏰ Przypomnę za 10 min`, `Nie znaleziono zadania`, `Najpierw połącz konto w Mindflow`. Po `done` dodatkowo `sendMessage` na ten czat: `✅ Ukończono: {tytuł}` (tytuł przez `HtmlEncode`).

- [ ] **Step 1: Write the failing test.** Fabryka `TelegramApiFactory`. Helper łączy czat jak w Task 2 (link + `/start` przez webhook), seeduje `TaskItem` właściciela (wzór seedowania: `factory.WithDbAsync(db => { db.Tasks.Add(...); ... })`), wysyła webhook z `callback_query`:

```csharp
new { update_id = 2, callback_query = new { id = "cb-1", from = new { id = chatId, username = "filip" }, message = new { message_id = 5, chat = new { id = chatId } }, data = $"done:{taskId:N}" } }
```

Testy:
1. `done` zamyka zadanie (`IsCompleted`, `Status == Completed`) i daje `answerCallbackQuery` z `text == "✅ Ukończono"`.
2. `snooze` ustawia `ReminderSnoozedUntil` w przedziale `[przed+10 min, po+10 min]` i daje odpowiedź `⏰ Przypomnę za 10 min`.
3. Przycisk z zadaniem innego użytkownika (zadanie innego konta, czat połączony z pierwszym) → zadanie nietknięte, odpowiedź `Nie znaleziono zadania`.
4. Przycisk z niepołączonego czatu → `Najpierw połącz konto w Mindflow`.
5. `TelegramPollingService` (bez webhooka): utwórz go ręcznie z `factory.Services` (`ActivatorUtilities.CreateInstance<TelegramPollingService>(factory.Services)`), dodaj do `factory.Telegram.UpdatesResults` jedną aktualizację `/start <kod>`, wywołaj publiczną metodę `PollOnceAsync(long offset, CancellationToken)` zwracającą następny offset i sprawdź: offset = `update_id + 1`, a status konta `connected`.

- [ ] **Step 2: Run** → FAIL.
- [ ] **Step 3: Implement.**
  - W `HandleUpdateAsync` dla `CallbackQuery`: czat = `CallbackQuery.Message?.Chat.Id ?? CallbackQuery.From.Id`, wiersz po `ChatId`. Brak → odpowiedź „Najpierw połącz…”. `Data` parsuj jako `{akcja}:{guid N}`, a niepoprawne traktuj jak brak zadania. `IReminderActionService.ApplyAsync(row.UserId, taskId, action, ct)`: `null` → „Nie znaleziono zadania”, w przeciwnym razie odpowiedź jak wyżej.
  - `TelegramPollingService : BackgroundService` (`IServiceScopeFactory`, `ILogger`). `ExecuteAsync`: raz `DeleteWebhookAsync`, potem pętla `offset = await PollOnceAsync(offset, ct)`, a przy wyjątku innym niż anulowanie log i `Task.Delay(5 s)`. `PollOnceAsync`: w nowym scope `TelegramApi.GetUpdatesAsync(offset, 25, ct)`, dla każdej aktualizacji `ITelegramService.HandleUpdateAsync` w osobnym scope, w `try/catch`, i zwraca `max(update_id) + 1` albo `offset`. W teście wołaj z `timeoutSeconds` zależnym od fałszywego handlera (handler odpowiada od razu, więc 25 nie spowalnia).
  - `IReminderActionService` musi działać bez żądania HTTP (polling). Etap 1 tworzy wtedy `DefaultHttpContext`. Jeśli tego nie robi, popraw `ReminderActionService.ActAs`: `httpContextAccessor.HttpContext ??= new DefaultHttpContext();` przed ustawieniem `User`.
- [ ] **Step 4: Run** pełne testy z DB → zielone.
- [ ] **Step 5: Commit** `feat: handle telegram reminder buttons and local polling`

---

### Task 4: Kanał Telegram

**Files:**
- Create: `src/Mindflow/Mindflow.Api/Services/Reminders/TelegramReminderChannel.cs`
- Modify: `src/Mindflow/Mindflow.Api/Extensions/ServiceCollectionExtensions.cs` (`services.AddScoped<IReminderChannel, TelegramReminderChannel>();`)
- Test: `src/Mindflow/Mindflow.Api.Tests/Telegram/TelegramReminderChannelTests.cs`

**Interfaces:**
- Consumes: `ReminderMessage(NotificationId, UserId, TaskId, Title, Body, LocalDue, DueAt, TaskUrl, ActionToken)`, `IReminderChannel`, `TelegramApi.SendMessageAsync`, `TelegramOptions`.
- Produces: kanał `Name = "telegram"`.

- [ ] **Step 1: Write the failing test** (fabryka `TelegramApiFactory`; połącz czat przez seed `TelegramConnection { UserId, ChatId, ConnectedAt }` w DB; kanał pobierz z `scope.ServiceProvider.GetServices<IReminderChannel>().Single(c => c.Name == "telegram")`):
  1. Połączony czat: `SendAsync` → `true`. Ostatnie `sendMessage` na `chat_id` ma tekst zaczynający się od `<b>⏰ Przypomnienie</b>`, zawierający zakodowany tytuł (dla tytułu `⏰ A <b> & C` jest `A &lt;b&gt; &amp; C`), przyciski `done:{taskId:N}` i `snooze:{taskId:N}`, a przy `TaskUrl = "https://app.test/zadania/1"` drugi wiersz z `url`.
  2. `TaskUrl = "http://localhost:4200/zadania/1"` → brak wiersza z `url`.
  3. `TelegramRemindersEnabled = false` w `NotificationSettings` albo brak połączenia → `false`, bez `sendMessage` na ten czat.
- [ ] **Step 2: Run** → FAIL.
- [ ] **Step 3: Implement.** Kanał: brak `TelegramOptions` → `false`; ustawienie użytkownika (domyślnie `true`), wiersz z `ChatId` (brak → `false`); tytuł bez prefiksu `⏰ `; `SendMessageAsync(chatId, html, keyboard, ct)` i zwraca jej wynik.
- [ ] **Step 4: Run** pełne testy z DB → zielone.
- [ ] **Step 5: Commit** `feat: send task reminders to telegram`

---

### Task 5: Dokumentacja

- [ ] Dopisz w `docs/push-notifications.md` sekcję „Telegram”: utworzenie bota w @BotFather (`/newbot`), zmienne `Telegram__BotToken`, `Telegram__BotUsername`, `Telegram__WebhookSecret`, jednorazowe ustawienie webhooka:

```bash
curl -X POST "https://api.telegram.org/bot$TELEGRAM_BOT_TOKEN/setWebhook" \
  -H 'Content-Type: application/json' \
  -d '{"url":"https://<api>/integrations/telegram/webhook","secret_token":"<Telegram__WebhookSecret>","allowed_updates":["message","callback_query"]}'
```

i lokalnie `Telegram__UsePolling=true` (bez webhooka). Commit `docs: describe telegram reminders`.
