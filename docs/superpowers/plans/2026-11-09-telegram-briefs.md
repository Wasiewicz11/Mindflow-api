# Briefy na Telegramie (API) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use subagent-driven-development (recommended) or executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Poranny brief, brief w południe i wieczorne podsumowanie przychodzą także od bota Telegrama (pogrubiony tytuł, treść, przycisk „Otwórz Mindflow”), również do użytkowników bez subskrypcji push.

**Architecture:** Nowy `TelegramNotifier` (scoped) znajduje czat użytkownika (konfiguracja bota, `TelegramRemindersEnabled`, połączony czat) i wysyła wiadomość z limitem czasu. `NotificationService.SendOnceAsync` dostaje flagę `viaTelegram`: brief i podsumowanie idą przez push **i** Telegram, a wpis w inboxie i dostawa zostają, gdy dotarł choć jeden kanał. Job obejmuje użytkowników z pushem albo z połączonym Telegramem, ale przypomnienia o blokach i zadaniach cyklicznych dalej liczy tylko dla użytkowników z pushem. Nowe przeciążenie `ProcessDueNotificationsAsync(DateTimeOffset now, …)` pozwala testować job w ustalonej chwili.

**Tech Stack:** .NET 10, EF Core (Postgres, snake_case), istniejący `TelegramApi` (typowany `HttpClient` z `RemoveAllLoggers`), xUnit + WebApplicationFactory (`TelegramApiFactory`, `FakeTelegramHandler`).

**Spec:** `/Users/filipwasiewicz/Projekty/Mindflow/mindflow-api-reminders/docs/superpowers/specs/2026-11-09-telegram-asystent-design.md` (sekcja 1)

## Global Constraints

- Worktree `/Users/filipwasiewicz/Projekty/Mindflow/mindflow-api-reminders`, gałąź `feature/telegram-briefs` od `feature/reminder-telegram` (już utworzona, zawiera commit ze specem).
- Commity: Conventional Commits po angielsku, małą literą, tryb rozkazujący, bez opisu, bez `Co-Authored-By`, bez wzmianek o AI. Test w tym samym commicie co kod. Zero komentarzy „dlaczego” w kodzie.
- Teksty dla użytkownika po polsku, dokładnie jak w zadaniach.
- Wiadomość Telegram dla briefu i podsumowania (HTML, tytuł i treść przez `WebUtility.HtmlEncode`): `<b>{tytuł}</b>\n{treść}`. Przycisk `Otwórz Mindflow` z adresem `{ReminderLinks.FrontendUrl}{ścieżka powiadomienia}` tylko wtedy, gdy adres zaczyna się od `https://`.
- `TelegramRemindersEnabled` znaczy teraz „powiadomienia na Telegram” w ogóle (przypomnienia, briefy, podsumowanie). Pole i etykieta w UI zostają bez zmian. Główny przełącznik `Enabled` i przełączniki/godziny briefów działają jak dotąd.
- Przypomnienia o blokach i zadaniach cyklicznych: bez zmian (tylko push).
- Token bota nigdy nie trafia do logów (klient już ma `RemoveAllLoggers`, nie loguj URL-i).
- Testy DB: `ConnectionStrings__Database='Host=localhost;Port=5435;Database=mindflow_reminders_tests;Username=postgres;Password=postgres'`. Fabryka nie migruje sama. Ta gałąź nie ma migracji.
- Rozmiar gałęzi: ≲ 20 plików i 1000 linii (bez migracji i `.md`). Ten plan: 7 plików kodu i testów.
- Build: `dotnet build src/Mindflow/Mindflow.sln`. Testy: `ConnectionStrings__Database='…' dotnet test src/Mindflow/Mindflow.sln`.

---

### Task 1: `TelegramNotifier`

**Files:**
- Create: `src/Mindflow/Mindflow.Api/Services/Telegram/TelegramNotifier.cs`
- Modify: `src/Mindflow/Mindflow.Api/Extensions/ServiceCollectionExtensions.cs` (`AddMindflowTelegram`)
- Modify: `src/Mindflow/Mindflow.Api.Tests/Infrastructure/TelegramApiFactory.cs` (ustawienie `Notifications:FrontendUrl`)
- Test: `src/Mindflow/Mindflow.Api.Tests/Telegram/TelegramNotifierTests.cs`

**Interfaces:**
- Consumes: `TelegramApi.SendMessageAsync(long chatId, string html, IReadOnlyList<IReadOnlyList<TelegramButton>>? keyboard, CancellationToken ct) : Task<bool>`, `TelegramOptions.From(IConfiguration)`, `ReminderLinks.FrontendUrl(IConfiguration)`, `NotificationSettings.TelegramRemindersEnabled`, `TelegramConnection.ChatId`.
- Produces (używane przez Task 2 i przez plan `telegram-ai-plan`):

```csharp
namespace Mindflow.Api.Services.Telegram;

public interface ITelegramNotifier
{
    Task<long?> FindChatAsync(Guid userId, CancellationToken ct);
    Task<bool> SendAsync(long chatId, string html, IReadOnlyList<IReadOnlyList<TelegramButton>>? keyboard, CancellationToken ct);
    Task<bool> SendNotificationAsync(Guid userId, string title, string body, string appPath, CancellationToken ct);
}
```

`FindChatAsync` zwraca `null`, gdy bot nie jest skonfigurowany, użytkownik ma `TelegramRemindersEnabled = false` albo nie ma połączonego czatu. `SendAsync` ogranicza wysyłkę do 10 s i przy przekroczeniu zwraca `false` z ostrzeżeniem w logu (bez tokenu). `SendNotificationAsync` = `FindChatAsync` + HTML `<b>{tytuł}</b>\n{treść}` + przycisk `Otwórz Mindflow` (tylko https). `appPath` to ścieżka zaczynająca się od `/`, np. `/?notification=…`.

- [ ] **Step 1: Ustaw adres frontu w fabryce testów**

W `TelegramApiFactory.Settings` dopisz jedną pozycję:

```csharp
        ["Notifications:FrontendUrl"] = "https://app.test"
```

(słownik po zmianie: `Telegram:BotToken`, `Telegram:BotUsername`, `Telegram:WebhookSecret`, `Notifications:FrontendUrl`). Istniejące testy Telegrama podają adresy wprost, więc to ustawienie ich nie zmienia.

- [ ] **Step 2: Write the failing test**

`src/Mindflow/Mindflow.Api.Tests/Telegram/TelegramNotifierTests.cs`:

```csharp
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Mindflow.Api.Models;
using Mindflow.Api.Services.Telegram;
using Mindflow.Api.Tests.Infrastructure;

namespace Mindflow.Api.Tests.Telegram;

public class TelegramNotifierTests(TelegramApiFactory factory) : IClassFixture<TelegramApiFactory>
{
    private static long chatSeed = Random.Shared.NextInt64(950_000_000, 990_000_000);

    [LocalDatabaseFact]
    public async Task Sends_bold_title_encoded_body_and_open_button()
    {
        var (userId, chatId) = await ConnectedAsync();

        var sent = await SendAsync(factory, userId, "Poranny <brief>", "A & B", "/?notification=abc");

        Assert.True(sent);
        var body = Assert.Single(Sent(chatId));
        Assert.Equal("<b>Poranny &lt;brief&gt;</b>\nA &amp; B", (string)body["text"]!);
        var button = body["reply_markup"]!["inline_keyboard"]![0]![0]!;
        Assert.Equal("Otwórz Mindflow", (string)button["text"]!);
        Assert.Equal("https://app.test/?notification=abc", (string)button["url"]!);
    }

    [LocalDatabaseFact]
    public async Task Omits_open_button_when_frontend_is_not_https()
    {
        var (userId, chatId) = await ConnectedAsync();
        using var app = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Notifications:FrontendUrl"] = "http://localhost:4200"
            })));

        Assert.True(await SendAsync(app, userId, "Podsumowanie dnia", "Ukończone dziś: 1.", "/?notification=abc"));

        Assert.Null(Assert.Single(Sent(chatId))["reply_markup"]);
    }

    [LocalDatabaseFact]
    public async Task Skips_user_with_telegram_notifications_off()
    {
        var (userId, chatId) = await ConnectedAsync();
        await factory.WithDbAsync(async db =>
        {
            db.NotificationSettings.Add(new NotificationSettings { UserId = userId, TelegramRemindersEnabled = false, UpdatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        });

        Assert.False(await SendAsync(factory, userId, "Poranny brief", "x", "/"));
        Assert.Empty(Sent(chatId));
    }

    [LocalDatabaseFact]
    public async Task Skips_user_without_connected_chat()
    {
        var user = await factory.CreateUserAsync();

        using var scope = factory.Services.CreateScope();
        var notifier = scope.ServiceProvider.GetRequiredService<ITelegramNotifier>();

        Assert.Null(await notifier.FindChatAsync(user.Id, CancellationToken.None));
        Assert.False(await notifier.SendNotificationAsync(user.Id, "Poranny brief", "x", "/", CancellationToken.None));
    }

    private static async Task<bool> SendAsync(WebApplicationFactory<Program> app, Guid userId, string title, string body, string path)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ITelegramNotifier>()
            .SendNotificationAsync(userId, title, body, path, CancellationToken.None);
    }

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

Uwaga: `TelegramApiFactory` dziedziczy po `WebApplicationFactory<Program>`, a `WithWebHostBuilder` zwraca `WebApplicationFactory<Program>`, więc helper przyjmuje oba. Fabryka pochodna z `WithWebHostBuilder` nadal woła `TelegramApiFactory.ConfigureWebHost`, więc używa tego samego `factory.Telegram`, a późniejsze `AddInMemoryCollection` nadpisuje adres frontu (tak samo robi `TelegramLoggingTests`).

- [ ] **Step 3: Run test to verify it fails**

Run: `ConnectionStrings__Database='Host=localhost;Port=5435;Database=mindflow_reminders_tests;Username=postgres;Password=postgres' dotnet test src/Mindflow/Mindflow.sln --filter TelegramNotifierTests`
Expected: błąd kompilacji (`ITelegramNotifier` nie istnieje).

- [ ] **Step 4: Write minimal implementation**

`src/Mindflow/Mindflow.Api/Services/Telegram/TelegramNotifier.cs`:

```csharp
using System.Net;
using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Data;
using Mindflow.Api.Services.Reminders;

namespace Mindflow.Api.Services.Telegram;

public interface ITelegramNotifier
{
    Task<long?> FindChatAsync(Guid userId, CancellationToken ct);
    Task<bool> SendAsync(long chatId, string html, IReadOnlyList<IReadOnlyList<TelegramButton>>? keyboard, CancellationToken ct);
    Task<bool> SendNotificationAsync(Guid userId, string title, string body, string appPath, CancellationToken ct);
}

public class TelegramNotifier(
    MindflowDbContext db,
    TelegramApi api,
    IConfiguration configuration,
    ILogger<TelegramNotifier> logger) : ITelegramNotifier
{
    private const string OpenButtonText = "Otwórz Mindflow";
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(10);

    public async Task<long?> FindChatAsync(Guid userId, CancellationToken ct)
    {
        if (TelegramOptions.From(configuration) is null) return null;

        var enabled = await db.NotificationSettings.AsNoTracking()
            .Where(settings => settings.UserId == userId)
            .Select(settings => (bool?)settings.TelegramRemindersEnabled)
            .SingleOrDefaultAsync(ct) ?? true;
        if (!enabled) return null;

        return await db.TelegramConnections.AsNoTracking()
            .Where(connection => connection.UserId == userId)
            .Select(connection => connection.ChatId)
            .SingleOrDefaultAsync(ct);
    }

    public async Task<bool> SendAsync(long chatId, string html, IReadOnlyList<IReadOnlyList<TelegramButton>>? keyboard, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(SendTimeout);
        try
        {
            return await api.SendMessageAsync(chatId, html, keyboard, timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Telegram notification timed out");
            return false;
        }
    }

    public async Task<bool> SendNotificationAsync(Guid userId, string title, string body, string appPath, CancellationToken ct)
    {
        if (await FindChatAsync(userId, ct) is not { } chatId) return false;

        var html = $"<b>{WebUtility.HtmlEncode(title)}</b>\n{WebUtility.HtmlEncode(body)}";
        var url = $"{ReminderLinks.FrontendUrl(configuration)}{appPath}";
        IReadOnlyList<IReadOnlyList<TelegramButton>>? keyboard = null;
        if (url.StartsWith("https://", StringComparison.Ordinal))
            keyboard = [[new TelegramButton(OpenButtonText, Url: url)]];

        return await SendAsync(chatId, html, keyboard, ct);
    }
}
```

W `ServiceCollectionExtensions.AddMindflowTelegram`, po `services.AddScoped<ITelegramService, TelegramService>();`:

```csharp
        services.AddScoped<ITelegramNotifier, TelegramNotifier>();
```

- [ ] **Step 5: Run test to verify it passes**

Run: `ConnectionStrings__Database='…' dotnet test src/Mindflow/Mindflow.sln --filter "TelegramNotifierTests|TelegramReminderChannelTests|TelegramLinkApiTests"`
Expected: PASS (4 nowe + dotychczasowe Telegrama).

- [ ] **Step 6: Commit**

```bash
git add src/Mindflow/Mindflow.Api/Services/Telegram/TelegramNotifier.cs \
  src/Mindflow/Mindflow.Api/Extensions/ServiceCollectionExtensions.cs \
  src/Mindflow/Mindflow.Api.Tests/Infrastructure/TelegramApiFactory.cs \
  src/Mindflow/Mindflow.Api.Tests/Telegram/TelegramNotifierTests.cs
git commit -m "feat: add telegram notifier for app notifications"
```

---

### Task 2: Briefy i podsumowanie przez push i Telegram

**Files:**
- Modify: `src/Mindflow/Mindflow.Api/Services/INotificationService.cs` (przeciążenie z `now`)
- Modify: `src/Mindflow/Mindflow.Api/Services/NotificationService.cs` (konstruktor, `ProcessDueNotificationsAsync`, `SendBriefOnceAsync`, `SendEveningSummaryOnceAsync`, `SendDueRecurringTaskRemindersAsync`, `SendDueBlockRemindersAsync` — tylko wywołania, `SendOnceAsync`)
- Test: `src/Mindflow/Mindflow.Api.Tests/Telegram/TelegramBriefsJobTests.cs`

**Interfaces:**
- Consumes: `ITelegramNotifier.SendNotificationAsync(Guid userId, string title, string body, string appPath, CancellationToken ct) : Task<bool>` (Task 1).
- Produces:

```csharp
Task<NotificationJobResponse> ProcessDueNotificationsAsync(DateTimeOffset now, CancellationToken ct = default);
```

dopisane do `INotificationService` (stara wersja bez `now` woła nową z `DateTimeOffset.UtcNow`). Plan `telegram-ai-plan` dopina się w tę samą pętlę zaraz po wysłaniu porannego briefu.

- [ ] **Step 1: Write the failing test**

`src/Mindflow/Mindflow.Api.Tests/Telegram/TelegramBriefsJobTests.cs`:

```csharp
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Services;
using Mindflow.Api.Tests.Infrastructure;

namespace Mindflow.Api.Tests.Telegram;

public class TelegramBriefsJobTests(TelegramApiFactory factory) : IClassFixture<TelegramApiFactory>
{
    private static readonly TimeZoneInfo Warsaw = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");
    private static readonly DateTimeOffset Now = new(DateTime.UtcNow.Date.AddHours(2).AddMinutes(30), TimeSpan.Zero);
    private static long chatSeed = Random.Shared.NextInt64(1_000_000_000, 2_000_000_000);

    [LocalDatabaseFact]
    public async Task Morning_brief_reaches_telegram_without_push_subscription()
    {
        var (userId, chatId) = await TelegramUserAsync(s => { s.MorningBriefEnabled = true; s.MorningBriefTime = LocalTime(Now); });
        await SeedTaskAsync(userId, "Raport <Q4> & podsumowanie", LocalDate(Now).AddDays(-1));

        var result = await RunAsync(Now);

        var item = Assert.Single(await InboxAsync(userId));
        Assert.Equal(NotificationInboxKind.MorningBrief, item.Kind);
        Assert.Equal("Dziś bez zadań z terminem. Zaległe (1): Raport <Q4> & podsumowanie.", item.Body);
        var message = Assert.Single(Sent(chatId));
        Assert.Equal("<b>Poranny brief</b>\nDziś bez zadań z terminem. Zaległe (1): Raport &lt;Q4&gt; &amp; podsumowanie.", (string)message["text"]!);
        var button = message["reply_markup"]!["inline_keyboard"]![0]![0]!;
        Assert.Equal("Otwórz Mindflow", (string)button["text"]!);
        Assert.Equal($"https://app.test/?notification={item.Id:N}", (string)button["url"]!);
        Assert.True(await DeliveredAsync(userId, $"brief:morning:{LocalDate(Now):yyyy-MM-dd}"));
        Assert.True(result.BriefsSent >= 1);
    }

    [LocalDatabaseFact]
    public async Task Brief_is_not_sent_twice_on_the_next_run()
    {
        var (userId, chatId) = await TelegramUserAsync(s => { s.MorningBriefEnabled = true; s.MorningBriefTime = LocalTime(Now); });

        await RunAsync(Now);
        await RunAsync(Now.AddMinutes(5));

        Assert.Single(await InboxAsync(userId));
        Assert.Single(Sent(chatId));
    }

    [LocalDatabaseFact]
    public async Task Evening_summary_reaches_telegram()
    {
        var (userId, chatId) = await TelegramUserAsync(s => { s.EveningSummaryEnabled = true; s.EveningSummaryTime = LocalTime(Now); });

        await RunAsync(Now);

        var item = Assert.Single(await InboxAsync(userId));
        Assert.Equal(NotificationInboxKind.EveningSummary, item.Kind);
        Assert.Equal($"<b>Podsumowanie dnia</b>\n{WebUtility.HtmlEncode(item.Body)}", (string)Assert.Single(Sent(chatId))["text"]!);
    }

    [LocalDatabaseFact]
    public async Task Respects_master_switch_and_telegram_switch()
    {
        var (offUserId, offChatId) = await TelegramUserAsync(s => { s.Enabled = false; s.MorningBriefEnabled = true; s.MorningBriefTime = LocalTime(Now); });
        var (mutedUserId, mutedChatId) = await TelegramUserAsync(s => { s.TelegramRemindersEnabled = false; s.MorningBriefEnabled = true; s.MorningBriefTime = LocalTime(Now); });

        await RunAsync(Now);

        Assert.Empty(Sent(offChatId));
        Assert.Empty(Sent(mutedChatId));
        Assert.Empty(await InboxAsync(offUserId));
        Assert.Empty(await InboxAsync(mutedUserId));
        Assert.False(await DeliveredAsync(mutedUserId, $"brief:morning:{LocalDate(Now):yyyy-MM-dd}"));
    }

    [LocalDatabaseFact]
    public async Task Block_reminders_stay_push_only()
    {
        var (userId, chatId) = await TelegramUserAsync(s => s.BlockRemindersEnabled = true);
        await factory.WithDbAsync(async db =>
        {
            db.CalendarBlocks.Add(new CalendarBlock
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Title = "Blok testowy",
                StartAt = Now.AddMinutes(10),
                DurationMinutes = 30,
                CreatedAt = Now,
                UpdatedAt = Now
            });
            await db.SaveChangesAsync();
        });

        await RunAsync(Now);

        Assert.Empty(Sent(chatId));
        Assert.Empty(await InboxAsync(userId));
    }

    private static TimeOnly LocalTime(DateTimeOffset utc)
    {
        var local = TimeZoneInfo.ConvertTime(utc, Warsaw);
        return new TimeOnly(local.Hour, local.Minute);
    }

    private static DateOnly LocalDate(DateTimeOffset utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utc, Warsaw).DateTime);

    private async Task<NotificationJobResponse> RunAsync(DateTimeOffset now)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<INotificationService>().ProcessDueNotificationsAsync(now);
    }

    private async Task<(Guid UserId, long ChatId)> TelegramUserAsync(Action<NotificationSettings> configure)
    {
        var user = await factory.CreateUserAsync();
        var chatId = Interlocked.Increment(ref chatSeed);
        var settings = new NotificationSettings
        {
            UserId = user.Id,
            MorningBriefEnabled = false,
            MiddayBriefEnabled = false,
            EveningSummaryEnabled = false,
            BlockRemindersEnabled = false,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        configure(settings);
        await factory.WithDbAsync(async db =>
        {
            db.NotificationSettings.Add(settings);
            db.TelegramConnections.Add(new TelegramConnection { UserId = user.Id, ChatId = chatId, ConnectedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        });
        return (user.Id, chatId);
    }

    private Task SeedTaskAsync(Guid userId, string content, DateOnly dueDate) =>
        factory.WithDbAsync(async db =>
        {
            db.Tasks.Add(new TaskItem { Id = Guid.NewGuid(), UserId = userId, Content = content, DueDate = dueDate, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        });

    private Task<List<NotificationInboxItem>> InboxAsync(Guid userId) =>
        factory.WithDbAsync(db => db.NotificationInboxItems.AsNoTracking().Where(i => i.UserId == userId).ToListAsync());

    private Task<bool> DeliveredAsync(Guid userId, string key) =>
        factory.WithDbAsync(db => db.PushNotificationDeliveries.AnyAsync(d => d.UserId == userId && d.DeliveryKey == key));

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

`Now` to stała chwila 02:30 UTC dzisiaj (03:30 albo 04:30 w Warszawie). Godziny domyślne (06:00, 13:00, 20:00) innych użytkowników w bazie wtedy nie wypadają, a plan `telegram-ai-plan` używa innej chwili (01:10 UTC), więc równoległe klasy testów nie wysyłają sobie nawzajem briefów.

- [ ] **Step 2: Run test to verify it fails**

Run: `ConnectionStrings__Database='…' dotnet test src/Mindflow/Mindflow.sln --filter TelegramBriefsJobTests`
Expected: błąd kompilacji (`ProcessDueNotificationsAsync(DateTimeOffset)` nie istnieje).

- [ ] **Step 3: Write minimal implementation**

`INotificationService` — pod istniejącą deklaracją `ProcessDueNotificationsAsync(CancellationToken ct = default)` dopisz:

```csharp
    Task<NotificationJobResponse> ProcessDueNotificationsAsync(DateTimeOffset now, CancellationToken ct = default);
```

`NotificationService`:

1. `using Mindflow.Api.Services.Telegram;` i nowy parametr konstruktora zaraz po `ITaskReminderService taskReminderService,`:

```csharp
    ITelegramNotifier telegramNotifier,
```

2. Zastąp początek `ProcessDueNotificationsAsync` (od sygnatury do `var userIds = …`):

```csharp
    public Task<NotificationJobResponse> ProcessDueNotificationsAsync(CancellationToken ct = default) =>
        ProcessDueNotificationsAsync(DateTimeOffset.UtcNow, ct);

    public async Task<NotificationJobResponse> ProcessDueNotificationsAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var pushUserIds = (await db.PushNotificationSubscriptions
            .AsNoTracking()
            .Select(subscription => subscription.UserId)
            .Distinct()
            .ToListAsync(ct)).ToHashSet();
        var users = await db.Users
            .AsNoTracking()
            .Where(user => db.PushNotificationSubscriptions.Any(subscription => subscription.UserId == user.Id)
                           || db.TelegramConnections.Any(connection => connection.UserId == user.Id && connection.ChatId != null))
            .ToListAsync(ct);
        var userIds = users.Select(user => user.Id).ToArray();
```

(usuń starą linię `var now = DateTimeOffset.UtcNow;` i stare zapytanie `users`).

3. W pętli przypomnienia cykliczne tylko dla użytkowników z pushem:

```csharp
            if (pushUserIds.Contains(user.Id))
                taskRemindersSent += await SendDueRecurringTaskRemindersAsync(
                    user.Id,
                    now,
                    GetTimeZoneOrUtc(user.TimeZone),
                    ct);
```

4. Bloki tylko dla użytkowników z pushem:

```csharp
        var blockRemindersSent = await SendDueBlockRemindersAsync(now, pushUserIds.ToArray(), settingsByUser, ct);
```

5. `SendOnceAsync` dostaje parametr `bool viaTelegram` przed `CancellationToken ct`:

```csharp
    private async Task<bool> SendOnceAsync(
        Guid userId,
        string deliveryKey,
        string title,
        string body,
        string tag,
        NotificationInboxKind? inboxKind,
        bool viaTelegram,
        CancellationToken ct)
```

i w środku zamień blok wysyłki:

```csharp
        bool sent;
        try
        {
            var pushed = await SendToUserAsync(userId, title, body, tag, targetUrl, ct);
            var telegramSent = viaTelegram && await telegramNotifier.SendNotificationAsync(userId, title, body, targetUrl, ct);
            sent = pushed || telegramSent;
        }
        catch
```

(reszta metody bez zmian: `catch` usuwa wpis i rzuca dalej, `!sent` usuwa wpis i zwraca `false`, potem zapis dostawy).

6. Wywołania `SendOnceAsync`: w `SendBriefOnceAsync` i `SendEveningSummaryOnceAsync` dopisz `true,` przed `ct`, a w `SendDueRecurringTaskRemindersAsync` i `SendDueBlockRemindersAsync` dopisz `false,` przed `ct`. Przykład dla briefu:

```csharp
        return await SendOnceAsync(
            userId,
            $"brief:{briefKind}:{localDate:yyyy-MM-dd}",
            title,
            body,
            $"mindflow:brief:{briefKind}",
            briefKind == "morning" ? NotificationInboxKind.MorningBrief : NotificationInboxKind.MiddayBrief,
            true,
            ct);
```

`SendTestAsync` i `SendToUserAsync` zostają bez zmian (test powiadomień dalej tylko push).

- [ ] **Step 4: Run test to verify it passes**

Run: `ConnectionStrings__Database='…' dotnet test src/Mindflow/Mindflow.sln`
Expected: PASS, wszystkie testy (5 nowych + dotychczasowe, w tym `NotificationInboxApiTests` i `TaskReminderJobTests`).

- [ ] **Step 5: Commit**

```bash
git add src/Mindflow/Mindflow.Api/Services/INotificationService.cs \
  src/Mindflow/Mindflow.Api/Services/NotificationService.cs \
  src/Mindflow/Mindflow.Api.Tests/Telegram/TelegramBriefsJobTests.cs
git commit -m "feat: send briefs and evening summary to telegram"
```

---

### Task 3: Dokumentacja

**Files:**
- Modify: `docs/push-notifications.md` (sekcja `## Telegram`)

- [ ] **Step 1: Dopisz akapit pod listą przycisków przypomnień w sekcji „Telegram”**

```markdown
Briefy (poranny i popołudniowy) oraz podsumowanie dnia też przychodzą od bota: pogrubiony tytuł, treść i przycisk `Otwórz Mindflow` (tylko gdy adres frontu jest na https). Idą równolegle z Web Push. Wpis w centrum powiadomień i znacznik dostawy powstają, gdy dotarł choć jeden kanał, więc kolejny przebieg joba niczego nie dubluje. Job obejmuje też konta bez subskrypcji push, jeśli mają połączony Telegram. Przełącznik `telegramRemindersEnabled` wyłącza teraz wszystkie powiadomienia na Telegram (przypomnienia, briefy, podsumowanie); etykieta w UI zostaje. Przypomnienia o blokach i zadaniach cyklicznych dalej idą tylko przez Web Push.
```

- [ ] **Step 2: Commit**

```bash
git add docs/push-notifications.md
git commit -m "docs: describe telegram briefs"
```

---

## Pokrycie specu (sekcja 1)

| Wymaganie | Zadanie |
|---|---|
| Brief przez push **i** Telegram, inbox i dostawa, gdy dotarł choć jeden kanał | Task 2 (`SendOnceAsync`), test `Morning_brief_reaches_telegram_without_push_subscription` |
| Job obejmuje użytkowników bez push z połączonym Telegramem | Task 2 (zapytanie `users`), ten sam test |
| Pogrubiony tytuł, treść enkodowana, przycisk „Otwórz Mindflow” tylko https | Task 1, testy `Sends_bold_title_…`, `Omits_open_button_…` |
| `Enabled`, przełączniki i godziny briefów, `TelegramRemindersEnabled` | Task 1 (`FindChatAsync`), Task 2 (pętla), test `Respects_master_switch_and_telegram_switch` |
| Podsumowanie wieczorne na Telegram | Task 2, test `Evening_summary_reaches_telegram` |
| Bez dublowania | test `Brief_is_not_sent_twice_on_the_next_run` |
| Przypomnienia o blokach i cykliczne bez zmian | Task 2 (`pushUserIds`), test `Block_reminders_stay_push_only` |
