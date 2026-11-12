# Plan dnia od AI na Telegramie (API) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use subagent-driven-development (recommended) or executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Zaraz po wysłaniu porannego briefu job generuje sugestie AI (istniejący `SuggestionService`) i wysyła je na Telegram: nagłówek „🧠 Plan dnia od AI”, a pod nim maks. 3 sugestie jako osobne wiadomości z przyciskami `Przyjmij` / `Odrzuć`, które działają tylko dla właściciela czatu.

**Architecture:** Nowy `TelegramAiPlan` (scoped). `SendAsync` sprawdza czat przez `ITelegramNotifier.FindChatAsync` (plan `telegram-briefs`), zajmuje klucz `telegram-ai-plan:<data>` w `push_notification_deliveries` (raz dziennie, także gdy AI nic nie da), woła `ISuggestionService.GenerateForUserAsync` (ścieżka crona: konfiguracja AI użytkownika, bez ręcznego limitu) i wysyła tylko sugestie z tego przebiegu, które pochodzą od dostawcy AI (nie z lokalnego `RuleBased`). `NotificationService` woła go w tej samej pętli zaraz po udanym porannym briefie. Przyciski `sg:ok:<id N>` / `sg:no:<id N>` idą przez `TelegramService.HandleCallbackAsync` do `TelegramAiPlan.HandleCallbackAsync`, który podszywa się pod właściciela czatu (`TelegramActor.ActAs` z planu `telegram-task-capture`) i woła istniejące `AcceptAsync` / `RejectAsync`, a potem zdejmuje klawiaturę (`RemoveKeyboardAsync`).

**Tech Stack:** .NET 10, EF Core (Postgres), istniejące `ISuggestionService`, `ITelegramNotifier`, `TelegramApi`, xUnit + WebApplicationFactory (`TelegramAiApiFactory`, `TelegramApiFactory`, `FakeTelegramHandler`, `FakeAiHandler`).

**Spec:** `/Users/filipwasiewicz/Projekty/Mindflow/mindflow-api-reminders/docs/superpowers/specs/2026-11-09-telegram-asystent-design.md` (sekcja 3)

**Wymaga:**
- `docs/superpowers/plans/2026-11-09-telegram-briefs.md` — `ITelegramNotifier` (`FindChatAsync`, `SendAsync`), `ProcessDueNotificationsAsync(DateTimeOffset now, …)`, poranny brief na Telegram.
- `docs/superpowers/plans/2026-11-09-telegram-task-capture.md` — `TelegramActor.ActAs`, `TelegramApi.RemoveKeyboardAsync`, `TelegramAiApiFactory`, routing `cap:` w `TelegramService.HandleCallbackAsync` (ten plan dopisuje obok `sg:`).

## Global Constraints

- Worktree `/Users/filipwasiewicz/Projekty/Mindflow/mindflow-api-reminders`. Gałąź: `git switch -c feature/telegram-ai-plan feature/telegram-task-capture`. MR zależy od MR `feature/telegram-task-capture`.
- Commity: Conventional Commits po angielsku, małą literą, tryb rozkazujący, bez opisu, bez `Co-Authored-By`, bez wzmianek o AI. Test w tym samym commicie co kod. Zero komentarzy „dlaczego” w kodzie.
- Teksty bota po polsku, dokładnie (stałe w `TelegramAiPlan`):
  - nagłówek: `<b>🧠 Plan dnia od AI</b>`
  - sugestia (HTML, przez `WebUtility.HtmlEncode`): `<b>{tytuł}</b>\n{treść}`; jeden wiersz przycisków `Przyjmij` (`sg:ok:{id:N}`) i `Odrzuć` (`sg:no:{id:N}`)
  - odpowiedzi na przyciski (`answerCallbackQuery`): `✅ Przyjęto`, `Odrzucono`, `Ta sugestia jest już nieaktualna.`
- Plan dostają tylko użytkownicy z połączonym Telegramem, włączonym porannym briefem (brief właśnie wyszedł) i `TelegramRemindersEnabled`. Maks. 3 sugestie.
- „AI nic nie zaproponowało albo zwróciło błąd” = w tym przebiegu nie powstała żadna oczekująca sugestia z `Provider` innym niż `RuleBased`. Wtedy plan nie wychodzi, a do logu trafia ostrzeżenie. Klucz `telegram-ai-plan:<data>` jest zajmowany przed wywołaniem AI, więc następne przebiegi joba tego dnia nie pytają AI ponownie.
- Przyjęcie i odrzucenie tylko przez istniejące `ISuggestionService.AcceptAsync` / `RejectAsync` (sprawdzają właściciela i status `Pending`).
- Testy DB: `ConnectionStrings__Database='Host=localhost;Port=5435;Database=mindflow_reminders_tests;Username=postgres;Password=postgres'`. Fabryka nie migruje sama. Ta gałąź nie ma migracji.
- Rozmiar gałęzi: ≲ 20 plików i 1000 linii (bez migracji i `.md`). Ten plan: 6 plików kodu i testów.
- Build: `dotnet build src/Mindflow/Mindflow.sln`. Testy: `ConnectionStrings__Database='…' dotnet test src/Mindflow/Mindflow.sln`.

---

### Task 1: Wysyłka planu po porannym briefie

**Files:**
- Create: `src/Mindflow/Mindflow.Api/Services/Telegram/TelegramAiPlan.cs`
- Modify: `src/Mindflow/Mindflow.Api/Services/NotificationService.cs` (konstruktor, blok porannego briefu w `ProcessDueNotificationsAsync`)
- Modify: `src/Mindflow/Mindflow.Api/Extensions/ServiceCollectionExtensions.cs` (`AddMindflowTelegram`)
- Test: `src/Mindflow/Mindflow.Api.Tests/Telegram/TelegramAiPlanJobTests.cs`

**Interfaces:**
- Consumes: `ITelegramNotifier.FindChatAsync(Guid userId, CancellationToken ct) : Task<long?>`, `ITelegramNotifier.SendAsync(long chatId, string html, IReadOnlyList<IReadOnlyList<TelegramButton>>? keyboard, CancellationToken ct) : Task<bool>`, `ISuggestionService.GenerateForUserAsync(Guid userId, CancellationToken ct = default) : Task<int>`, `AiSuggestion { Id, UserId, Title, Body, Status, Provider, CreatedAt }`, `PushNotificationDelivery { Id, UserId, DeliveryKey, SentAt }` (unikalny indeks `(UserId, DeliveryKey)`), `INotificationService.ProcessDueNotificationsAsync(DateTimeOffset now, CancellationToken ct = default)`.
- Produces:

```csharp
namespace Mindflow.Api.Services.Telegram;

public interface ITelegramAiPlan
{
    Task<bool> SendAsync(Guid userId, DateOnly localDate, CancellationToken ct);
}
```

`SendAsync` nigdy nie rzuca przy błędzie AI (zwraca `false` i loguje ostrzeżenie). Task 2 dopisze do interfejsu `HandleCallbackAsync`.

- [ ] **Step 1: Write the failing test**

`src/Mindflow/Mindflow.Api.Tests/Telegram/TelegramAiPlanJobTests.cs`:

```csharp
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Services;
using Mindflow.Api.Tests.Infrastructure;

namespace Mindflow.Api.Tests.Telegram;

public class TelegramAiPlanJobTests(TelegramAiApiFactory factory) : IClassFixture<TelegramAiApiFactory>
{
    private static readonly TimeZoneInfo Warsaw = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");
    private static readonly DateTimeOffset Now = new(DateTime.UtcNow.Date.AddHours(1).AddMinutes(10), TimeSpan.Zero);
    private static long chatSeed = Random.Shared.NextInt64(100_000_000_000, 200_000_000_000);

    [LocalDatabaseFact]
    public async Task Plan_follows_the_morning_brief_once_a_day()
    {
        var (userId, chatId) = await TelegramUserAsync();
        await SeedTaskAsync(userId, "Raport kwartalny", LocalDate(Now).AddDays(-3), TaskPriority.P2);
        factory.Ai.Respond((_, _) => Suggestions("""[{"title":"Domknij raport","body":"Raport <Q4> jest po terminie.","actions":[{"taskRef":1,"actionType":"ChangePriority","payload":{"priority":"P1"}}]}]"""));
        var before = factory.Ai.Requests.Count;

        await RunAsync(Now);
        await RunAsync(Now.AddMinutes(5));

        var sent = Sent(chatId);
        Assert.Equal(3, sent.Count);
        Assert.StartsWith("<b>Poranny brief</b>", (string)sent[0]["text"]!);
        Assert.Equal("<b>🧠 Plan dnia od AI</b>", (string)sent[1]["text"]!);
        Assert.Equal("<b>Domknij raport</b>\nRaport &lt;Q4&gt; jest po terminie.", (string)sent[2]["text"]!);
        var suggestion = await factory.WithDbAsync(db => db.AiSuggestions.AsNoTracking()
            .SingleAsync(s => s.UserId == userId && s.Status == SuggestionStatus.Pending));
        var row = sent[2]["reply_markup"]!["inline_keyboard"]![0]!.AsArray();
        Assert.Equal(("Przyjmij", $"sg:ok:{suggestion.Id:N}"), ((string)row[0]!["text"]!, (string)row[0]!["callback_data"]!));
        Assert.Equal(("Odrzuć", $"sg:no:{suggestion.Id:N}"), ((string)row[1]!["text"]!, (string)row[1]!["callback_data"]!));
        Assert.Equal(1, factory.Ai.Requests.Count - before);
        Assert.True(await DeliveredAsync(userId, $"telegram-ai-plan:{LocalDate(Now):yyyy-MM-dd}"));
    }

    [LocalDatabaseFact]
    public async Task No_plan_when_ai_suggests_nothing()
    {
        var (userId, chatId) = await TelegramUserAsync();
        await SeedTaskAsync(userId, "Spokojne zadanie", LocalDate(Now).AddDays(5), TaskPriority.P3);
        factory.Ai.Respond((_, _) => Suggestions("[]"));

        await RunAsync(Now);

        var sent = Sent(chatId);
        Assert.StartsWith("<b>Poranny brief</b>", (string)Assert.Single(sent)["text"]!);
        Assert.True(await DeliveredAsync(userId, $"telegram-ai-plan:{LocalDate(Now):yyyy-MM-dd}"));
    }

    [LocalDatabaseFact]
    public async Task Local_fallback_is_not_sent_as_an_ai_plan()
    {
        var (userId, chatId) = await TelegramUserAsync();
        await SeedTaskAsync(userId, "Raport kwartalny", LocalDate(Now).AddDays(-3), TaskPriority.P2);
        factory.Ai.Respond((_, _) => FakeAiHandler.Json("""{"error":{"message":"boom"}}""", HttpStatusCode.InternalServerError));

        await RunAsync(Now);

        Assert.Single(Sent(chatId));
        Assert.True(await factory.WithDbAsync(db => db.AiSuggestions.AnyAsync(s => s.UserId == userId && s.Provider == "RuleBased")));
    }

    [LocalDatabaseFact]
    public async Task No_plan_and_no_ai_call_when_telegram_notifications_are_off()
    {
        var (userId, chatId) = await TelegramUserAsync(telegramEnabled: false);
        await SeedTaskAsync(userId, "Raport kwartalny", LocalDate(Now).AddDays(-3), TaskPriority.P2);
        var before = factory.Ai.Requests.Count;

        await RunAsync(Now);

        Assert.Empty(Sent(chatId));
        Assert.Equal(before, factory.Ai.Requests.Count);
        Assert.False(await DeliveredAsync(userId, $"telegram-ai-plan:{LocalDate(Now):yyyy-MM-dd}"));
    }

    private static HttpResponseMessage Suggestions(string suggestionsJson) =>
        FakeAiHandler.Json(JsonSerializer.Serialize(new
        {
            candidates = new[] { new { content = new { parts = new[] { new { text = $$"""{"suggestions":{{suggestionsJson}}}""" } } } } }
        }));

    private static TimeOnly LocalTime(DateTimeOffset utc)
    {
        var local = TimeZoneInfo.ConvertTime(utc, Warsaw);
        return new TimeOnly(local.Hour, local.Minute);
    }

    private static DateOnly LocalDate(DateTimeOffset utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utc, Warsaw).DateTime);

    private async Task RunAsync(DateTimeOffset now)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<INotificationService>().ProcessDueNotificationsAsync(now);
    }

    private async Task<(Guid UserId, long ChatId)> TelegramUserAsync(bool telegramEnabled = true)
    {
        var user = await factory.CreateUserAsync();
        var chatId = Interlocked.Increment(ref chatSeed);
        await factory.WithDbAsync(async db =>
        {
            db.NotificationSettings.Add(new NotificationSettings
            {
                UserId = user.Id,
                MorningBriefEnabled = true,
                MorningBriefTime = LocalTime(Now),
                MiddayBriefEnabled = false,
                EveningSummaryEnabled = false,
                BlockRemindersEnabled = false,
                TelegramRemindersEnabled = telegramEnabled,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            db.TelegramConnections.Add(new TelegramConnection { UserId = user.Id, ChatId = chatId, ConnectedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        });
        return (user.Id, chatId);
    }

    private Task SeedTaskAsync(Guid userId, string content, DateOnly dueDate, TaskPriority priority) =>
        factory.WithDbAsync(async db =>
        {
            db.Tasks.Add(new TaskItem { Id = Guid.NewGuid(), UserId = userId, Content = content, DueDate = dueDate, Priority = priority, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        });

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

`Now` to 01:10 UTC dzisiaj, inna chwila niż w `TelegramBriefsJobTests` (02:30 UTC), żeby równoległe klasy testów nie wysyłały sobie nawzajem briefów: job przetwarza wszystkich użytkowników w bazie. Pierwszy test sprawdza też kolejność (brief, nagłówek, sugestia) i to, że drugi przebieg nie pyta AI ponownie. Trzeci test: Gemini zwraca 500, orkiestrator spada na lokalny `RuleBased` (zadanie zaległe), a taka sugestia nie jest wysyłana.

- [ ] **Step 2: Run test to verify it fails**

Run: `ConnectionStrings__Database='…' dotnet test src/Mindflow/Mindflow.sln --filter TelegramAiPlanJobTests`
Expected: FAIL w trzech testach: `Plan_follows_the_morning_brief_once_a_day` (wychodzi tylko brief), `No_plan_when_ai_suggests_nothing` (brak klucza `telegram-ai-plan:…`) i `Local_fallback_is_not_sent_as_an_ai_plan` (nikt nie generuje sugestii). `No_plan_and_no_ai_call_when_telegram_notifications_are_off` przechodzi już teraz i pilnuje, żeby po zmianie tak zostało.

- [ ] **Step 3: Write minimal implementation**

`src/Mindflow/Mindflow.Api/Services/Telegram/TelegramAiPlan.cs`:

```csharp
using System.Net;
using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Data;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Enums;

namespace Mindflow.Api.Services.Telegram;

public interface ITelegramAiPlan
{
    Task<bool> SendAsync(Guid userId, DateOnly localDate, CancellationToken ct);
}

public class TelegramAiPlan(
    MindflowDbContext db,
    ITelegramNotifier notifier,
    ISuggestionService suggestions,
    ILogger<TelegramAiPlan> logger) : ITelegramAiPlan
{
    public const string HeaderText = "<b>🧠 Plan dnia od AI</b>";

    private const int MaxSuggestions = 3;
    private const string LocalProvider = "RuleBased";

    public async Task<bool> SendAsync(Guid userId, DateOnly localDate, CancellationToken ct)
    {
        if (await notifier.FindChatAsync(userId, ct) is not { } chatId) return false;
        if (!await ClaimAsync(userId, $"telegram-ai-plan:{localDate:yyyy-MM-dd}", ct)) return false;

        var startedAt = DateTimeOffset.UtcNow;
        try
        {
            await suggestions.GenerateForUserAsync(userId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "AI plan generation failed for user {UserId}.", userId);
            return false;
        }

        var plan = await db.AiSuggestions.AsNoTracking()
            .Where(s => s.UserId == userId
                        && s.Status == SuggestionStatus.Pending
                        && s.CreatedAt >= startedAt
                        && s.Provider != LocalProvider)
            .OrderBy(s => s.CreatedAt)
            .ThenBy(s => s.Title)
            .Take(MaxSuggestions)
            .ToListAsync(ct);
        if (plan.Count == 0)
        {
            logger.LogWarning("AI plan for user {UserId} has no AI suggestions.", userId);
            return false;
        }

        if (!await notifier.SendAsync(chatId, HeaderText, null, ct)) return false;
        foreach (var suggestion in plan)
            await notifier.SendAsync(chatId, Format(suggestion), Buttons(suggestion.Id), ct);
        return true;
    }

    private static string Format(AiSuggestion suggestion) =>
        $"<b>{WebUtility.HtmlEncode(suggestion.Title)}</b>\n{WebUtility.HtmlEncode(suggestion.Body)}";

    private static IReadOnlyList<IReadOnlyList<TelegramButton>> Buttons(Guid suggestionId) =>
    [[
        new TelegramButton("Przyjmij", CallbackData: $"sg:ok:{suggestionId:N}"),
        new TelegramButton("Odrzuć", CallbackData: $"sg:no:{suggestionId:N}")
    ]];

    private async Task<bool> ClaimAsync(Guid userId, string deliveryKey, CancellationToken ct)
    {
        if (await db.PushNotificationDeliveries.AnyAsync(d => d.UserId == userId && d.DeliveryKey == deliveryKey, ct))
            return false;

        var delivery = new PushNotificationDelivery
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            DeliveryKey = deliveryKey,
            SentAt = DateTimeOffset.UtcNow
        };
        db.PushNotificationDeliveries.Add(delivery);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            db.Entry(delivery).State = EntityState.Detached;
            return false;
        }
    }
}
```

`NotificationService`:

1. Nowy parametr konstruktora zaraz po `ITelegramNotifier telegramNotifier,`:

```csharp
    ITelegramAiPlan telegramAiPlan,
```

2. W `ProcessDueNotificationsAsync` zamień blok porannego briefu:

```csharp
            if (settings.MorningBriefEnabled && IsWithinScheduleWindow(localTime, settings.MorningBriefTime))
            {
                if (await SendBriefOnceAsync(user.Id, localDate, "morning", "Poranny brief", ct))
                {
                    briefsSent++;
                    await telegramAiPlan.SendAsync(user.Id, localDate, ct);
                }
            }
```

`ServiceCollectionExtensions.AddMindflowTelegram`, po `services.AddScoped<ITelegramNotifier, TelegramNotifier>();`:

```csharp
        services.AddScoped<ITelegramAiPlan, TelegramAiPlan>();
```

- [ ] **Step 4: Run test to verify it passes**

Run: `ConnectionStrings__Database='…' dotnet test src/Mindflow/Mindflow.sln --filter "TelegramAiPlanJobTests|TelegramBriefsJobTests|SuggestionsAiApiTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Mindflow/Mindflow.Api/Services/Telegram/TelegramAiPlan.cs \
  src/Mindflow/Mindflow.Api/Services/NotificationService.cs \
  src/Mindflow/Mindflow.Api/Extensions/ServiceCollectionExtensions.cs \
  src/Mindflow/Mindflow.Api.Tests/Telegram/TelegramAiPlanJobTests.cs
git commit -m "feat: send daily ai plan to telegram after morning brief"
```

---

### Task 2: Przyciski `Przyjmij` / `Odrzuć`

**Files:**
- Modify: `src/Mindflow/Mindflow.Api/Services/Telegram/TelegramAiPlan.cs`
- Modify: `src/Mindflow/Mindflow.Api/Services/Telegram/TelegramService.cs` (konstruktor, `HandleCallbackAsync`)
- Test: `src/Mindflow/Mindflow.Api.Tests/Telegram/TelegramAiPlanButtonsTests.cs`

**Interfaces:**
- Consumes: `ISuggestionService.AcceptAsync(Guid suggestionId) : Task<bool>`, `ISuggestionService.RejectAsync(Guid suggestionId) : Task<bool>` (obie biorą użytkownika z `ICurrentUserService` i zwracają `false` dla cudzej albo nie-`Pending` sugestii), `TelegramActor.ActAs(IHttpContextAccessor accessor, Guid userId)`, `TelegramApi.AnswerCallbackQueryAsync(string callbackQueryId, string text, CancellationToken ct)`, `TelegramApi.RemoveKeyboardAsync(long chatId, long messageId, CancellationToken ct)`.
- Produces:

```csharp
public interface ITelegramAiPlan
{
    Task<bool> SendAsync(Guid userId, DateOnly localDate, CancellationToken ct);
    Task HandleCallbackAsync(Guid userId, TelegramCallbackQuery callback, CancellationToken ct);
}
```

- [ ] **Step 1: Write the failing test**

`src/Mindflow/Mindflow.Api.Tests/Telegram/TelegramAiPlanButtonsTests.cs`:

```csharp
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
```

Przyjęcie nie woła AI, więc wystarczy `TelegramApiFactory` (serwisy AI są zarejestrowane zawsze).

- [ ] **Step 2: Run test to verify it fails**

Run: `ConnectionStrings__Database='…' dotnet test src/Mindflow/Mindflow.sln --filter TelegramAiPlanButtonsTests`
Expected: FAIL — `sg:…` trafia dziś do parsera przypomnień i dostaje `Nie znaleziono zadania`.

- [ ] **Step 3: Write minimal implementation**

`TelegramAiPlan.cs`:

1. Interfejs:

```csharp
public interface ITelegramAiPlan
{
    Task<bool> SendAsync(Guid userId, DateOnly localDate, CancellationToken ct);
    Task HandleCallbackAsync(Guid userId, TelegramCallbackQuery callback, CancellationToken ct);
}
```

2. Konstruktor (dochodzą `TelegramApi api` i `IHttpContextAccessor httpContextAccessor`):

```csharp
public class TelegramAiPlan(
    MindflowDbContext db,
    ITelegramNotifier notifier,
    ISuggestionService suggestions,
    TelegramApi api,
    IHttpContextAccessor httpContextAccessor,
    ILogger<TelegramAiPlan> logger) : ITelegramAiPlan
```

3. Nowe stałe:

```csharp
    public const string AcceptedText = "✅ Przyjęto";
    public const string RejectedText = "Odrzucono";
    public const string StaleText = "Ta sugestia jest już nieaktualna.";
```

4. Nowa metoda:

```csharp
    public async Task HandleCallbackAsync(Guid userId, TelegramCallbackQuery callback, CancellationToken ct)
    {
        var parts = callback.Data?.Split(':');
        if (parts is not ["sg", var verb, var id] || verb is not ("ok" or "no") || !Guid.TryParseExact(id, "N", out var suggestionId))
        {
            await api.AnswerCallbackQueryAsync(callback.Id, StaleText, ct);
            return;
        }

        TelegramActor.ActAs(httpContextAccessor, userId);
        var accept = verb == "ok";
        var decided = accept ? await suggestions.AcceptAsync(suggestionId) : await suggestions.RejectAsync(suggestionId);
        if (!decided)
        {
            await api.AnswerCallbackQueryAsync(callback.Id, StaleText, ct);
            return;
        }

        await api.AnswerCallbackQueryAsync(callback.Id, accept ? AcceptedText : RejectedText, ct);
        if (callback.Message is { } message)
            await api.RemoveKeyboardAsync(message.Chat.Id, message.MessageId, ct);
    }
```

`TelegramService` — nowy parametr konstruktora po `ITelegramTaskCapture capture,`:

```csharp
    ITelegramAiPlan aiPlan,
```

i w `HandleCallbackAsync`, zaraz po bloku przekierowania `cap:` (z planu `telegram-task-capture`):

```csharp
        if (callback.Data?.StartsWith("sg:", StringComparison.Ordinal) == true)
        {
            await aiPlan.HandleCallbackAsync(row.UserId, callback, ct);
            return;
        }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `ConnectionStrings__Database='…' dotnet test src/Mindflow/Mindflow.sln`
Expected: PASS (3 nowe + wszystkie dotychczasowe, w tym `TelegramButtonsApiTests` i `TelegramDraftButtonsApiTests`).

- [ ] **Step 5: Commit**

```bash
git add src/Mindflow/Mindflow.Api/Services/Telegram/TelegramAiPlan.cs \
  src/Mindflow/Mindflow.Api/Services/Telegram/TelegramService.cs \
  src/Mindflow/Mindflow.Api.Tests/Telegram/TelegramAiPlanButtonsTests.cs
git commit -m "feat: accept or reject ai plan suggestions from telegram"
```

---

### Task 3: Dokumentacja

**Files:**
- Modify: `docs/push-notifications.md` (sekcja `## Telegram`)

- [ ] **Step 1: Dopisz podsekcję po „Dodawanie zadań z bota”**

```markdown
### Plan dnia od AI

Zaraz po porannym briefie (ten sam przebieg joba `POST /internal/jobs/notifications`) API generuje sugestie tym samym silnikiem co „Przeanalizuj dzień”, z konfiguracją AI użytkownika (ścieżka crona, bez ręcznego limitu), i wysyła je osobno: wiadomość „🧠 Plan dnia od AI”, a pod nią maks. 3 sugestie, każda z przyciskami `Przyjmij` i `Odrzuć`. Przyjęcie wykonuje akcje sugestii tak jak w aplikacji; po kliknięciu bot zdejmuje przyciski. Plan dostają tylko konta z połączonym Telegramem, włączonym porannym briefem i `telegramRemindersEnabled`.

Plan wychodzi raz dziennie (klucz `telegram-ai-plan:<data>` w `push_notification_deliveries`, zajmowany przed wywołaniem AI). Gdy AI nic nie zaproponuje albo zwróci błąd (wtedy zostają tylko lokalne sugestie `RuleBased`), plan nie jest wysyłany, a w logu jest ostrzeżenie `AI plan for user … has no AI suggestions.` albo `AI plan generation failed for user ….`.
```

- [ ] **Step 2: Commit**

```bash
git add docs/push-notifications.md
git commit -m "docs: describe daily ai plan on telegram"
```

---

## Pokrycie specu (sekcja 3)

| Wymaganie | Zadanie |
|---|---|
| Po porannym briefie, ten sam przebieg joba, klucz `telegram-ai-plan:<data>` | Task 1 (hook w `NotificationService`, `ClaimAsync`), test `Plan_follows_the_morning_brief_once_a_day` |
| Sugestie przez istniejący `SuggestionService` z konfiguracją AI użytkownika | Task 1 (`GenerateForUserAsync`) |
| Nagłówek „🧠 Plan dnia od AI”, każda sugestia osobno z `Przyjmij` / `Odrzuć` (`sg:ok:<id N>` / `sg:no:<id N>`), maks. 3 | Task 1 (`HeaderText`, `Buttons`, `MaxSuggestions`) |
| Przyjęcie i odrzucenie przez istniejącą logikę, tylko dla właściciela czatu | Task 2, testy `Accept_…`, `Reject_…`, `Suggestion_of_another_user_is_rejected` |
| Po kliknięciu zdjęcie przycisków i „✅ Przyjęto” / „Odrzucono” | Task 2 (`RemoveKeyboardAsync`, `answerCallbackQuery`) |
| Tylko połączony Telegram + poranny brief + `TelegramRemindersEnabled` | Task 1 (`FindChatAsync`, wywołanie po udanym briefie), test `No_plan_and_no_ai_call_when_telegram_notifications_are_off` |
| AI nic nie da albo błąd → brak planu, ostrzeżenie w logu | Task 1, testy `No_plan_when_ai_suggests_nothing`, `Local_fallback_is_not_sent_as_an_ai_plan` |
