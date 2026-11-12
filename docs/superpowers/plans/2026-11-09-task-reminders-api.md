# Task Reminders (API, etap 1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use subagent-driven-development (recommended) or executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Zadanie z terminem i godziną ma własne przypomnienie (minuty przed / wyłączone / odłożone), które job wysyła do aplikacji (inbox + SignalR) i na Web Push z akcjami Zrobione / Odłóż 10 min.

**Architecture:** Pola na `TaskItem`, czysta funkcja `ReminderSchedule` liczy, czy przypomnienie jest należne, `TaskReminderService` (wołany z istniejącego `NotificationService.ProcessDueNotificationsAsync`) deduplikuje przez `push_notification_deliveries` i oddaje `ReminderMessage` do `ReminderDispatcher`, który woła każdy `IReminderChannel`. Akcje z powiadomień idą przez anonimowy endpoint z podpisanym tokenem HMAC.

**Tech Stack:** .NET 10, ASP.NET Core, EF Core 10 + Npgsql (snake_case przez EFCore.NamingConventions), WebPush 1.0.13, SignalR, xUnit + WebApplicationFactory.

**Spec:** `/Users/filipwasiewicz/Projekty/Mindflow/mindflow-web-reminders/docs/superpowers/specs/2026-11-09-przypomnienia-design.md`

## Global Constraints

- Repo `mindflow-api`, worktree `/Users/filipwasiewicz/Projekty/Mindflow/mindflow-api-reminders`, gałąź `feature/task-reminders` (od `origin/feat/space-scoped-integration`).
- Kod w `src/Mindflow/Mindflow.Api`, testy w `src/Mindflow/Mindflow.Api.Tests`.
- Commity: Conventional Commits po angielsku, małą literą, tryb rozkazujący, bez opisu, **bez `Co-Authored-By`** i bez wzmianek o AI. Test w tym samym commicie co kod.
- W kodzie zero komentarzy wyjaśniających „dlaczego”. Najwyżej krótki `/// <summary>` jak w `TaskItem`.
- Teksty dla użytkownika po polsku, komunikaty wyjątków po angielsku (jak w repo).
- `ReminderMinutesBefore` w zakresie 0–10080. Okno wysyłki 15 min. Odłożenie 10 min. Token ważny do `max(termin, teraz) + 2 dni`.
- Testy bazodanowe: `[LocalDatabaseFact]`, zmienna `ConnectionStrings__Database` wskazuje lokalny Postgres (np. `Host=localhost;Port=5434;Database=mindflow_reminders_tests;Username=postgres;Password=postgres`). Bez niej testy bazodanowe są pomijane, a nie zielone, więc przed raportem uruchom je z bazą.
- Build: `dotnet build src/Mindflow/Mindflow.sln`. Testy: `dotnet test src/Mindflow/Mindflow.sln`.

## Mapa plików

| Plik | Odpowiedzialność |
|---|---|
| `Models/TaskItem.cs` (mod) | pola `ReminderMinutesBefore`, `ReminderDisabled`, `ReminderSnoozedUntil` |
| `Models/Dtos/TaskDtos.cs` (mod) | pola w create/update/list/detail |
| `Services/TaskService.cs` (mod) | ustawianie pól, zerowanie odłożenia |
| `Services/TaskResponseMapper.cs` (mod) | mapowanie pól |
| `Migrations/*_AddTaskReminders.cs` (gen) | kolumny |
| `Services/Reminders/LocalTime.cs` | lokalna data i godzina → UTC (DST) |
| `Services/Reminders/ReminderSchedule.cs` | czy i kiedy przypomnienie jest należne, klucz dedup |
| `Services/Reminders/ReminderText.cs` | tytuł i treść po polsku |
| `Services/Reminders/ReminderActionTokens.cs` | token HMAC dla akcji |
| `Services/Reminders/ReminderLinks.cs` | adresy frontu i endpointu akcji z konfiguracji |
| `Services/Reminders/ReminderMessage.cs` | rekord wiadomości + `IReminderChannel` |
| `Services/Reminders/ReminderDispatcher.cs` | woła kanały, izoluje błędy |
| `Services/Reminders/InAppReminderChannel.cs` | inbox + SignalR `ReminderDue` |
| `Services/Reminders/PushReminderChannel.cs` | Web Push z akcjami |
| `Services/Reminders/TaskReminderService.cs` | wybór zadań, dedup, wysyłka |
| `Services/Reminders/ReminderActionService.cs` | Zrobione / Odłóż |
| `Services/WebPushSender.cs` | wydzielona wysyłka Web Push (używa jej `NotificationService` i kanał push) |
| `Controllers/ReminderActionsController.cs` | `POST /notifications/reminder-actions` |
| `Hubs/ITasksNotifier.cs`, `Hubs/TasksNotifier.cs` (mod) | `ReminderDueAsync` |
| `Models/Enums/NotificationInboxKind.cs` (mod) | `TaskReminder` |
| `Services/NotificationService.cs` (mod) | używa `IWebPushSender` i `ITaskReminderService` zamiast starego `SendDueTaskRemindersAsync` |
| `Extensions/ServiceCollectionExtensions.cs` (mod) | rejestracje DI |

---

### Task 1: Pola przypomnienia w zadaniu

**Files:**
- Modify: `src/Mindflow/Mindflow.Api/Models/TaskItem.cs`
- Modify: `src/Mindflow/Mindflow.Api/Models/Dtos/TaskDtos.cs`
- Modify: `src/Mindflow/Mindflow.Api/Services/TaskService.cs` (CreateAsync ok. l. 86–104, UpdateAsync ok. l. 150–180)
- Modify: `src/Mindflow/Mindflow.Api/Services/TaskResponseMapper.cs`
- Create (generated): `src/Mindflow/Mindflow.Api/Migrations/<ts>_AddTaskReminders.cs` + Designer + snapshot
- Test: `src/Mindflow/Mindflow.Api.Tests/TaskReminderFieldsApiTests.cs`

**Interfaces:**
- Produces: `TaskItem.ReminderMinutesBefore (int?)`, `TaskItem.ReminderDisabled (bool)`, `TaskItem.ReminderSnoozedUntil (DateTimeOffset?)`; JSON `reminderMinutesBefore`, `reminderDisabled` w odpowiedziach zadań; w `PUT /tasks/{id}` pola `reminderMinutesBefore`, `clearReminderMinutesBefore`, `reminderDisabled`; w `POST /tasks` pola `reminderMinutesBefore`, `reminderDisabled`.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Tests.Infrastructure;

namespace Mindflow.Api.Tests;

public class TaskReminderFieldsApiTests(MindflowApiFactory factory) : IClassFixture<MindflowApiFactory>
{
    [LocalDatabaseFact]
    public async Task Create_stores_own_reminder_and_returns_it()
    {
        var client = await CreateClientAsync();

        var created = await CreateTaskAsync(client, new
        {
            content = "Telefon do księgowej",
            dueDate = "2030-01-10",
            dueTime = "14:00:00",
            reminderMinutesBefore = 30
        });

        Assert.Equal(30, created.ReminderMinutesBefore);
        Assert.False(created.ReminderDisabled);
    }

    [LocalDatabaseFact]
    public async Task Update_can_disable_and_reset_reminder_to_default()
    {
        var client = await CreateClientAsync();
        var created = await CreateTaskAsync(client, new
        {
            content = "Spotkanie", dueDate = "2030-01-10", dueTime = "09:00:00", reminderMinutesBefore = 60
        });

        var disabled = await UpdateTaskAsync(client, created.Id, new { reminderDisabled = true });
        Assert.True(disabled.ReminderDisabled);
        Assert.Equal(60, disabled.ReminderMinutesBefore);

        var reset = await UpdateTaskAsync(client, created.Id, new { clearReminderMinutesBefore = true, reminderDisabled = false });
        Assert.Null(reset.ReminderMinutesBefore);
        Assert.False(reset.ReminderDisabled);
    }

    [LocalDatabaseFact]
    public async Task Changing_due_time_clears_snooze()
    {
        var client = await CreateClientAsync();
        var created = await CreateTaskAsync(client, new { content = "Lek", dueDate = "2030-01-10", dueTime = "08:00:00" });
        await factory.WithDbAsync(async db =>
        {
            var task = await db.Tasks.SingleAsync(t => t.Id == created.Id);
            task.ReminderSnoozedUntil = DateTimeOffset.UtcNow.AddMinutes(10);
            await db.SaveChangesAsync();
        });

        await UpdateTaskAsync(client, created.Id, new { dueTime = "08:30:00" });

        var snoozed = await factory.WithDbAsync(db =>
            db.Tasks.Where(t => t.Id == created.Id).Select(t => t.ReminderSnoozedUntil).SingleAsync());
        Assert.Null(snoozed);
    }

    [LocalDatabaseFact]
    public async Task Rejects_reminder_longer_than_a_week()
    {
        var client = await CreateClientAsync();

        var response = await client.PostAsJsonAsync("/tasks", new
        {
            content = "Za daleko", dueDate = "2030-01-10", dueTime = "08:00:00", reminderMinutesBefore = 10081
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<HttpClient> CreateClientAsync()
    {
        var user = await factory.CreateUserAsync();
        return factory.CreateUserClient(user.Id);
    }

    private static async Task<TaskDetailResponse> CreateTaskAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/tasks", body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TaskDetailResponse>(MindflowApiFactory.Json))!;
    }

    private static async Task<TaskDetailResponse> UpdateTaskAsync(HttpClient client, Guid id, object body)
    {
        var response = await client.PutAsJsonAsync($"/tasks/{id}", body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TaskDetailResponse>(MindflowApiFactory.Json))!;
    }
}
```

Sprawdź w `TasksController`, czy `POST /tasks` zwraca `TaskDetailResponse` (Created) i czy `PUT /tasks/{id}` zwraca `TaskDetailResponse`. Jeśli kształt jest inny, dopasuj tylko deserializację w helperach.

- [ ] **Step 2: Run test to verify it fails**

Run: `ConnectionStrings__Database='Host=localhost;Port=5434;Database=mindflow_reminders_tests;Username=postgres;Password=postgres' dotnet test src/Mindflow/Mindflow.sln --filter TaskReminderFieldsApiTests`
Expected: kompilacja się nie udaje (`ReminderMinutesBefore` nie istnieje w `TaskDetailResponse`).

- [ ] **Step 3: Implement**

`TaskItem.cs`, po `EstimatedHours`:

```csharp
    /// <summary>Minutes before the due time; null means the user's default.</summary>
    public int? ReminderMinutesBefore { get; set; }
    public bool ReminderDisabled { get; set; }
    public DateTimeOffset? ReminderSnoozedUntil { get; set; }
```

`TaskDtos.cs`:
- `CreateTaskRequest`: dopisz na końcu parametry `[Range(0, 10080)] int? ReminderMinutesBefore = null, bool? ReminderDisabled = null`.
- `UpdateTaskRequest`: dopisz za `bool? IsCompleted = null` parametry `[Range(0, 10080)] int? ReminderMinutesBefore = null, bool ClearReminderMinutesBefore = false, bool? ReminderDisabled = null`.
- `TaskListResponse` i `TaskDetailResponse`: dopisz `int? ReminderMinutesBefore, bool ReminderDisabled` tuż przed `DateTimeOffset CreatedAt`.

`TaskResponseMapper.cs`: w obu metodach przed `task.CreatedAt` dodaj `task.ReminderMinutesBefore, task.ReminderDisabled,`. Potem `grep -rn "new TaskListResponse\|new TaskDetailResponse" src/` i popraw każde inne miejsce, które tworzy te rekordy (tak samo).

`TaskService.CreateAsync`, w inicjalizatorze `TaskItem` po `EstimatedHours`:

```csharp
            ReminderMinutesBefore = request.ReminderMinutesBefore,
            ReminderDisabled = request.ReminderDisabled ?? false,
```

`TaskService.UpdateAsync`: zaraz po linii `var previousTags = task.Tags.ToList();` dodaj

```csharp
        var previousReminder = (task.DueDate, task.DueTime, task.ReminderMinutesBefore, task.ReminderDisabled);
```

a po linii `else if (request.EstimatedHours.HasValue) task.EstimatedHours = request.EstimatedHours;` dodaj

```csharp
        if (request.ClearReminderMinutesBefore) task.ReminderMinutesBefore = null;
        else if (request.ReminderMinutesBefore.HasValue) task.ReminderMinutesBefore = request.ReminderMinutesBefore;
        if (request.ReminderDisabled.HasValue) task.ReminderDisabled = request.ReminderDisabled.Value;
        if (previousReminder != (task.DueDate, task.DueTime, task.ReminderMinutesBefore, task.ReminderDisabled))
            task.ReminderSnoozedUntil = null;
```

Migracja (z katalogu worktree):

```bash
dotnet ef migrations add AddTaskReminders \
  --project src/Mindflow/Mindflow.Api/Mindflow.Api.csproj \
  --startup-project src/Mindflow/Mindflow.Api/Mindflow.Api.csproj
```

Sprawdź, że migracja dodaje tylko 3 kolumny do `tasks`: `reminder_minutes_before integer NULL`, `reminder_disabled boolean NOT NULL DEFAULT false`, `reminder_snoozed_until timestamp with time zone NULL`. Jeśli EF dorzuca inne zmiany (rozjazd snapshotu), zatrzymaj się i zgłoś to.

- [ ] **Step 4: Run tests**

Run: `ConnectionStrings__Database=... dotnet test src/Mindflow/Mindflow.sln`
Expected: wszystkie zielone (nowe 4 + dotychczasowe). Testy bazodanowe stosują migracje same (sprawdź w `MindflowApiFactory`). Jeśli tego nie robią, uruchom `dotnet ef database update` z tym connection stringiem.

- [ ] **Step 5: Commit**

```bash
git add -A src/Mindflow
git commit -m "feat: add per-task reminder settings"
```

---

### Task 2: Harmonogram przypomnienia (czysta logika)

**Files:**
- Create: `src/Mindflow/Mindflow.Api/Services/Reminders/LocalTime.cs`
- Create: `src/Mindflow/Mindflow.Api/Services/Reminders/ReminderSchedule.cs`
- Create: `src/Mindflow/Mindflow.Api/Services/Reminders/ReminderText.cs`
- Test: `src/Mindflow/Mindflow.Api.Tests/Reminders/ReminderScheduleTests.cs`, `src/Mindflow/Mindflow.Api.Tests/Reminders/ReminderTextTests.cs`

**Interfaces:**
- Consumes: pola z Task 1, `NotificationSettings` (`Enabled`, `BlockRemindersEnabled`, `BlockReminderMinutes`).
- Produces:
  - `LocalTime.ToUtc(DateOnly date, TimeOnly time, TimeZoneInfo tz) : DateTimeOffset?` (null dla nieistniejącej godziny przy zmianie czasu)
  - `record DueReminder(DateTimeOffset DueAt, DateTimeOffset RemindAt, int MinutesBefore, bool IsSnoozed, string DeliveryKey)`
  - `ReminderSchedule.Grace : TimeSpan` (15 min), `ReminderSchedule.ResolveMinutesBefore(TaskItem, NotificationSettings) : int?`, `ReminderSchedule.FindDue(TaskItem, NotificationSettings, TimeZoneInfo, DateTimeOffset now) : DueReminder?`
  - `ReminderText.Build(string taskTitle, DueReminder due, TimeZoneInfo tz, DateTimeOffset now) : (string Title, string Body, string LocalDue)`

- [ ] **Step 1: Write the failing tests**

```csharp
using Mindflow.Api.Models;
using Mindflow.Api.Services.Reminders;
using TaskStatus = Mindflow.Api.Models.Enums.TaskStatus;

namespace Mindflow.Api.Tests.Reminders;

public class ReminderScheduleTests
{
    private static readonly TimeZoneInfo Warsaw = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");
    private static readonly NotificationSettings Defaults = new();

    private static TaskItem Task(string date = "2030-01-10", string time = "14:00", int? minutes = null) => new()
    {
        Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Content = "Telefon",
        DueDate = DateOnly.Parse(date),
        DueTime = TimeOnly.Parse(time),
        ReminderMinutesBefore = minutes
    };

    private static DateTimeOffset WarsawTime(string value) =>
        new(DateTime.Parse(value), Warsaw.GetUtcOffset(DateTime.Parse(value)));

    [Fact]
    public void Default_reminder_fires_ten_minutes_before_within_grace()
    {
        Assert.Null(ReminderSchedule.FindDue(Task(), Defaults, Warsaw, WarsawTime("2030-01-10 13:49")));
        var due = ReminderSchedule.FindDue(Task(), Defaults, Warsaw, WarsawTime("2030-01-10 13:50"));
        Assert.NotNull(due);
        Assert.Equal(10, due.MinutesBefore);
        Assert.NotNull(ReminderSchedule.FindDue(Task(), Defaults, Warsaw, WarsawTime("2030-01-10 14:04")));
        Assert.Null(ReminderSchedule.FindDue(Task(), Defaults, Warsaw, WarsawTime("2030-01-10 14:05")));
    }

    [Fact]
    public void Own_zero_minutes_fires_at_due_time()
    {
        var due = ReminderSchedule.FindDue(Task(minutes: 0), Defaults, Warsaw, WarsawTime("2030-01-10 14:00"));
        Assert.Equal(WarsawTime("2030-01-10 14:00"), due!.RemindAt);
    }

    [Fact]
    public void Own_minutes_ignore_disabled_default()
    {
        var settings = new NotificationSettings { BlockRemindersEnabled = false };
        Assert.Null(ReminderSchedule.FindDue(Task(), settings, Warsaw, WarsawTime("2030-01-10 13:50")));
        Assert.NotNull(ReminderSchedule.FindDue(Task(minutes: 60), settings, Warsaw, WarsawTime("2030-01-10 13:00")));
    }

    [Fact]
    public void Disabled_completed_or_globally_off_never_fire()
    {
        var now = WarsawTime("2030-01-10 13:50");
        var disabled = Task(); disabled.ReminderDisabled = true;
        var completed = Task(); completed.Status = TaskStatus.Completed; completed.IsCompleted = true;
        Assert.Null(ReminderSchedule.FindDue(disabled, Defaults, Warsaw, now));
        Assert.Null(ReminderSchedule.FindDue(completed, Defaults, Warsaw, now));
        Assert.Null(ReminderSchedule.FindDue(Task(), new NotificationSettings { Enabled = false }, Warsaw, now));
    }

    [Fact]
    public void Snooze_replaces_original_moment_and_changes_key()
    {
        var task = Task();
        task.ReminderSnoozedUntil = WarsawTime("2030-01-10 14:10");
        Assert.Null(ReminderSchedule.FindDue(task, Defaults, Warsaw, WarsawTime("2030-01-10 13:50")));
        var due = ReminderSchedule.FindDue(task, Defaults, Warsaw, WarsawTime("2030-01-10 14:11"));
        Assert.True(due!.IsSnoozed);
        Assert.Contains(":snooze:", due.DeliveryKey);
    }

    [Fact]
    public void Delivery_key_changes_with_minutes()
    {
        var a = ReminderSchedule.FindDue(Task(minutes: 10), Defaults, Warsaw, WarsawTime("2030-01-10 13:50"));
        var b = ReminderSchedule.FindDue(Task(minutes: 15), Defaults, Warsaw, WarsawTime("2030-01-10 13:45"));
        Assert.NotEqual(a!.DeliveryKey, b!.DeliveryKey);
    }

    [Fact]
    public void Nonexistent_local_time_on_spring_forward_is_skipped()
    {
        Assert.Null(LocalTime.ToUtc(new DateOnly(2030, 3, 31), new TimeOnly(2, 30), Warsaw));
        Assert.NotNull(LocalTime.ToUtc(new DateOnly(2030, 10, 27), new TimeOnly(2, 30), Warsaw));
    }
}
```

```csharp
using Mindflow.Api.Services.Reminders;

namespace Mindflow.Api.Tests.Reminders;

public class ReminderTextTests
{
    private static readonly TimeZoneInfo Warsaw = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");

    private static DateTimeOffset At(string value) =>
        new(DateTime.Parse(value), Warsaw.GetUtcOffset(DateTime.Parse(value)));

    [Theory]
    [InlineData(0, "Teraz", "dziś 14:00")]
    [InlineData(10, "Za 10 min", "dziś 14:00")]
    [InlineData(90, "Za 1 h 30 min", "dziś 14:00")]
    [InlineData(120, "Za 2 h", "dziś 14:00")]
    [InlineData(1440, "Za 1 dzień", "jutro 14:00")]
    public void Body_says_how_long_until_due(int minutes, string lead, string localDue)
    {
        var due = new DueReminder(At("2030-01-10 14:00"), At("2030-01-10 14:00").AddMinutes(-minutes), minutes, false, "k");
        var text = ReminderText.Build("Telefon", due, Warsaw, At("2030-01-10 14:00").AddMinutes(-minutes));

        Assert.Equal("⏰ Telefon", text.Title);
        Assert.StartsWith(lead, text.Body);
        Assert.Contains(localDue, text.Body);
        Assert.Equal(localDue, text.LocalDue);
    }

    [Fact]
    public void Tomorrow_and_other_days_are_named()
    {
        var due = new DueReminder(At("2030-01-11 09:00"), At("2030-01-10 09:00"), 1440, false, "k");
        Assert.Equal("jutro 09:00", ReminderText.Build("X", due, Warsaw, At("2030-01-10 09:00")).LocalDue);
        var later = new DueReminder(At("2030-01-14 09:00"), At("2030-01-10 09:00"), 5760, false, "k");
        Assert.Equal("14.01 09:00", ReminderText.Build("X", later, Warsaw, At("2030-01-10 09:00")).LocalDue);
    }

    [Fact]
    public void Snoozed_reminder_says_so_and_long_titles_are_cut()
    {
        var due = new DueReminder(At("2030-01-10 14:00"), At("2030-01-10 14:10"), 10, true, "k");
        var text = ReminderText.Build(new string('a', 200), due, Warsaw, At("2030-01-10 14:10"));
        Assert.StartsWith("Odłożone przypomnienie", text.Body);
        Assert.True(text.Title.Length <= 82);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test src/Mindflow/Mindflow.sln --filter "ReminderScheduleTests|ReminderTextTests"`
Expected: błąd kompilacji (brak `Mindflow.Api.Services.Reminders`).

- [ ] **Step 3: Implement**

`LocalTime.cs`:

```csharp
namespace Mindflow.Api.Services.Reminders;

public static class LocalTime
{
    public static DateTimeOffset? ToUtc(DateOnly date, TimeOnly time, TimeZoneInfo timeZone)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        if (timeZone.IsInvalidTime(local)) return null;

        if (timeZone.IsAmbiguousTime(local))
        {
            var offset = timeZone.GetAmbiguousTimeOffsets(local).Max();
            return new DateTimeOffset(local, offset).ToUniversalTime();
        }

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, timeZone));
    }
}
```

`ReminderSchedule.cs`:

```csharp
using Mindflow.Api.Models;
using TaskStatus = Mindflow.Api.Models.Enums.TaskStatus;

namespace Mindflow.Api.Services.Reminders;

public record DueReminder(DateTimeOffset DueAt, DateTimeOffset RemindAt, int MinutesBefore, bool IsSnoozed, string DeliveryKey);

public static class ReminderSchedule
{
    public static readonly TimeSpan Grace = TimeSpan.FromMinutes(15);

    public static int? ResolveMinutesBefore(TaskItem task, NotificationSettings settings)
    {
        if (task.ReminderDisabled) return null;
        if (task.ReminderMinutesBefore is int own) return own;
        return settings.BlockRemindersEnabled ? settings.BlockReminderMinutes : null;
    }

    public static DueReminder? FindDue(TaskItem task, NotificationSettings settings, TimeZoneInfo timeZone, DateTimeOffset now)
    {
        if (!settings.Enabled || task.ReminderDisabled) return null;
        if (task.IsCompleted || task.Status == TaskStatus.Completed) return null;
        if (task.DueDate is not DateOnly date || task.DueTime is not TimeOnly time) return null;

        var dueAt = LocalTime.ToUtc(date, time, timeZone);
        if (dueAt is null) return null;

        if (task.ReminderSnoozedUntil is DateTimeOffset snoozedUntil)
        {
            return IsInWindow(snoozedUntil, now)
                ? new DueReminder(dueAt.Value, snoozedUntil, task.ReminderMinutesBefore ?? 0, true,
                    $"task:{task.Id:N}:snooze:{snoozedUntil.UtcTicks}")
                : null;
        }

        var minutes = ResolveMinutesBefore(task, settings);
        if (minutes is null) return null;

        var remindAt = dueAt.Value.AddMinutes(-minutes.Value);
        return IsInWindow(remindAt, now)
            ? new DueReminder(dueAt.Value, remindAt, minutes.Value, false,
                $"task:{task.Id:N}:{date:yyyy-MM-dd}:{time:HH\\:mm}:r{minutes.Value}")
            : null;
    }

    private static bool IsInWindow(DateTimeOffset moment, DateTimeOffset now) =>
        moment <= now && now < moment + Grace;
}
```

`ReminderText.cs`:

```csharp
using System.Globalization;

namespace Mindflow.Api.Services.Reminders;

public static class ReminderText
{
    public static (string Title, string Body, string LocalDue) Build(
        string taskTitle, DueReminder due, TimeZoneInfo timeZone, DateTimeOffset now)
    {
        var localDue = FormatLocalDue(due.DueAt, now, timeZone);
        var lead = due.IsSnoozed
            ? "Odłożone przypomnienie"
            : due.MinutesBefore == 0 ? "Teraz" : $"Za {FormatLead(due.MinutesBefore)}";
        return ($"⏰ {Truncate(taskTitle, 80)}", $"{lead} · termin {localDue}", localDue);
    }

    private static string FormatLocalDue(DateTimeOffset dueAt, DateTimeOffset now, TimeZoneInfo timeZone)
    {
        var localDue = TimeZoneInfo.ConvertTime(dueAt, timeZone);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, timeZone).DateTime);
        var dueDay = DateOnly.FromDateTime(localDue.DateTime);
        var time = localDue.ToString("HH:mm", CultureInfo.InvariantCulture);
        if (dueDay == today) return $"dziś {time}";
        if (dueDay == today.AddDays(1)) return $"jutro {time}";
        return $"{localDue.ToString("dd.MM", CultureInfo.InvariantCulture)} {time}";
    }

    private static string FormatLead(int minutes)
    {
        if (minutes < 60) return $"{minutes} min";
        if (minutes % 1440 == 0) return minutes == 1440 ? "1 dzień" : $"{minutes / 1440} dni";
        var hours = minutes / 60;
        var rest = minutes % 60;
        return rest == 0 ? $"{hours} h" : $"{hours} h {rest} min";
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : $"{value[..(maxLength - 1)]}…";
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test src/Mindflow/Mindflow.sln --filter "ReminderScheduleTests|ReminderTextTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Mindflow/Mindflow.Api/Services/Reminders src/Mindflow/Mindflow.Api.Tests/Reminders
git commit -m "feat: compute when a task reminder is due"
```

---

### Task 3: Token akcji przypomnienia

**Files:**
- Create: `src/Mindflow/Mindflow.Api/Services/Reminders/ReminderActionTokens.cs`
- Test: `src/Mindflow/Mindflow.Api.Tests/Reminders/ReminderActionTokensTests.cs`

**Interfaces:**
- Produces: `record ReminderActionClaims(Guid TaskId, Guid UserId, DateTimeOffset ExpiresAt)`, `interface IReminderActionTokens { string Create(Guid taskId, Guid userId, DateTimeOffset expiresAt); ReminderActionClaims? Validate(string? token, DateTimeOffset now); }`, klasa `ReminderActionTokens(IConfiguration)` (czyta `Jwt:Secret`, rzuca `InvalidOperationException`, gdy pusty).

- [ ] **Step 1: Write the failing test**

```csharp
using Microsoft.Extensions.Configuration;
using Mindflow.Api.Services.Reminders;

namespace Mindflow.Api.Tests.Reminders;

public class ReminderActionTokensTests
{
    private static ReminderActionTokens Create(string secret = "secret-0123456789-0123456789-0123456789") =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Secret"] = secret }).Build());

    private static readonly Guid TaskId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2030, 1, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Valid_token_round_trips()
    {
        var tokens = Create();
        var claims = tokens.Validate(tokens.Create(TaskId, UserId, Now.AddDays(2)), Now);
        Assert.Equal((TaskId, UserId), (claims!.TaskId, claims.UserId));
    }

    [Fact]
    public void Expired_token_is_rejected()
    {
        var tokens = Create();
        Assert.Null(tokens.Validate(tokens.Create(TaskId, UserId, Now.AddMinutes(-1)), Now));
    }

    [Fact]
    public void Tampered_or_foreign_tokens_are_rejected()
    {
        var tokens = Create();
        var token = tokens.Create(TaskId, UserId, Now.AddDays(2));
        var other = tokens.Create(Guid.NewGuid(), UserId, Now.AddDays(2));
        var swapped = other.Split('.')[0] + "." + token.Split('.')[1];

        Assert.Null(tokens.Validate(swapped, Now));
        Assert.Null(tokens.Validate(token[..^2] + "AA", Now));
        Assert.Null(Create("another-secret-0123456789-0123456789").Validate(token, Now));
        Assert.Null(tokens.Validate("garbage", Now));
        Assert.Null(tokens.Validate(null, Now));
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test src/Mindflow/Mindflow.sln --filter ReminderActionTokensTests`
Expected: błąd kompilacji.

- [ ] **Step 3: Implement**

```csharp
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace Mindflow.Api.Services.Reminders;

public record ReminderActionClaims(Guid TaskId, Guid UserId, DateTimeOffset ExpiresAt);

public interface IReminderActionTokens
{
    string Create(Guid taskId, Guid userId, DateTimeOffset expiresAt);
    ReminderActionClaims? Validate(string? token, DateTimeOffset now);
}

public class ReminderActionTokens(IConfiguration configuration) : IReminderActionTokens
{
    private readonly Lazy<byte[]> key = new(() =>
    {
        var secret = configuration["Jwt:Secret"];
        if (string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException("Jwt:Secret is not configured.");
        return SHA256.HashData(Encoding.UTF8.GetBytes($"mindflow-reminder-actions:{secret}"));
    });

    public string Create(Guid taskId, Guid userId, DateTimeOffset expiresAt)
    {
        var payload = Encoding.UTF8.GetBytes($"{taskId:N}.{userId:N}.{expiresAt.ToUnixTimeSeconds()}");
        return $"{WebEncoders.Base64UrlEncode(payload)}.{WebEncoders.Base64UrlEncode(Sign(payload))}";
    }

    public ReminderActionClaims? Validate(string? token, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 512) return null;
        var parts = token.Split('.');
        if (parts.Length != 2) return null;

        byte[] payload, signature;
        try
        {
            payload = WebEncoders.Base64UrlDecode(parts[0]);
            signature = WebEncoders.Base64UrlDecode(parts[1]);
        }
        catch (FormatException)
        {
            return null;
        }

        if (!CryptographicOperations.FixedTimeEquals(signature, Sign(payload))) return null;

        var fields = Encoding.UTF8.GetString(payload).Split('.');
        if (fields.Length != 3
            || !Guid.TryParseExact(fields[0], "N", out var taskId)
            || !Guid.TryParseExact(fields[1], "N", out var userId)
            || !long.TryParse(fields[2], out var expiresUnix))
            return null;

        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(expiresUnix);
        return expiresAt <= now ? null : new ReminderActionClaims(taskId, userId, expiresAt);
    }

    private byte[] Sign(byte[] payload) => HMACSHA256.HashData(key.Value, payload);
}
```

- [ ] **Step 4: Run tests** — `dotnet test src/Mindflow/Mindflow.sln --filter ReminderActionTokensTests` → PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Mindflow/Mindflow.Api/Services/Reminders/ReminderActionTokens.cs src/Mindflow/Mindflow.Api.Tests/Reminders/ReminderActionTokensTests.cs
git commit -m "feat: sign reminder action tokens"
```

---

### Task 4: Kanały i dispatcher (aplikacja + Web Push)

**Files:**
- Create: `src/Mindflow/Mindflow.Api/Services/WebPushSender.cs`
- Create: `src/Mindflow/Mindflow.Api/Services/Reminders/ReminderMessage.cs`
- Create: `src/Mindflow/Mindflow.Api/Services/Reminders/ReminderLinks.cs`
- Create: `src/Mindflow/Mindflow.Api/Services/Reminders/ReminderDispatcher.cs`
- Create: `src/Mindflow/Mindflow.Api/Services/Reminders/InAppReminderChannel.cs`
- Create: `src/Mindflow/Mindflow.Api/Services/Reminders/PushReminderChannel.cs`
- Modify: `src/Mindflow/Mindflow.Api/Services/NotificationService.cs` (wysyłka Web Push przez `IWebPushSender`)
- Modify: `src/Mindflow/Mindflow.Api/Hubs/ITasksNotifier.cs`, `Hubs/TasksNotifier.cs`
- Modify: `src/Mindflow/Mindflow.Api/Models/Enums/NotificationInboxKind.cs`
- Modify: `src/Mindflow/Mindflow.Api/Extensions/ServiceCollectionExtensions.cs`
- Test: `src/Mindflow/Mindflow.Api.Tests/Reminders/ReminderDispatcherTests.cs`, `src/Mindflow/Mindflow.Api.Tests/Reminders/ReminderLinksTests.cs`

**Interfaces:**
- Consumes: `IReminderActionTokens` (Task 3).
- Produces:
  - `record ReminderMessage(Guid UserId, Guid TaskId, string Title, string Body, string LocalDue, DateTimeOffset DueAt, string TaskUrl, string ActionToken)`
  - `interface IReminderChannel { string Name { get; } Task<bool> SendAsync(ReminderMessage message, CancellationToken ct); }`
  - `interface IReminderDispatcher { Task<IReadOnlyList<string>> DispatchAsync(ReminderMessage message, CancellationToken ct); }` (zwraca nazwy kanałów, które dostarczyły)
  - `interface IWebPushSender { Task<bool> SendAsync(Guid userId, object payload, CancellationToken ct); }`
  - `ReminderLinks.FrontendUrl(IConfiguration) : string`, `ReminderLinks.TaskUrl(IConfiguration, Guid taskId) : string`, `ReminderLinks.ActionApiUrl(IConfiguration) : string`, `ReminderLinks.ActionPageUrl(IConfiguration, string token) : string`
  - `record ReminderDuePayload(Guid NotificationId, Guid TaskId, string Title, string Body, DateTimeOffset DueAt, string ActionToken)`, `ITasksNotifier.ReminderDueAsync(Guid userId, ReminderDuePayload payload)` → zdarzenie SignalR `ReminderDue`
  - `NotificationInboxKind.TaskReminder`

- [ ] **Step 1: Write the failing tests**

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Mindflow.Api.Services.Reminders;

namespace Mindflow.Api.Tests.Reminders;

public class ReminderDispatcherTests
{
    private sealed class FakeChannel(string name, Func<bool> send) : IReminderChannel
    {
        public string Name => name;
        public int Calls { get; private set; }
        public Task<bool> SendAsync(ReminderMessage message, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(send());
        }
    }

    private static readonly ReminderMessage Message = new(
        Guid.NewGuid(), Guid.NewGuid(), "⏰ X", "Za 10 min · termin dziś 14:00", "dziś 14:00",
        DateTimeOffset.UtcNow, "https://app/zadania/1", "token");

    [Fact]
    public async Task Failing_channel_does_not_stop_the_others()
    {
        var broken = new FakeChannel("email", () => throw new InvalidOperationException("smtp down"));
        var silent = new FakeChannel("push", () => false);
        var app = new FakeChannel("app", () => true);
        var dispatcher = new ReminderDispatcher([broken, silent, app], NullLogger<ReminderDispatcher>.Instance);

        var delivered = await dispatcher.DispatchAsync(Message, CancellationToken.None);

        Assert.Equal(["app"], delivered);
        Assert.Equal((1, 1, 1), (broken.Calls, silent.Calls, app.Calls));
    }
}
```

```csharp
using Microsoft.Extensions.Configuration;
using Mindflow.Api.Services.Reminders;

namespace Mindflow.Api.Tests.Reminders;

public class ReminderLinksTests
{
    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value)).Build();

    [Fact]
    public void Uses_notification_urls_and_falls_back_to_cors()
    {
        var taskId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var explicitConfig = Config(("Notifications:FrontendUrl", "https://app.test/"), ("Notifications:PublicApiUrl", "https://api.test/"));
        var fallback = Config(("Cors:FrontendUrl", "http://localhost:4200"));

        Assert.Equal("https://app.test/zadania/22222222-2222-2222-2222-222222222222", ReminderLinks.TaskUrl(explicitConfig, taskId));
        Assert.Equal("https://api.test/notifications/reminder-actions", ReminderLinks.ActionApiUrl(explicitConfig));
        Assert.Equal("http://localhost:4200/przypomnienie?t=a%2Bb", ReminderLinks.ActionPageUrl(fallback, "a+b"));
        Assert.Equal("/notifications/reminder-actions", ReminderLinks.ActionApiUrl(fallback));
    }
}
```

- [ ] **Step 2: Run to verify failure** — `dotnet test src/Mindflow/Mindflow.sln --filter "ReminderDispatcherTests|ReminderLinksTests"` → błąd kompilacji.

- [ ] **Step 3: Implement**

`ReminderMessage.cs`:

```csharp
namespace Mindflow.Api.Services.Reminders;

public record ReminderMessage(
    Guid UserId,
    Guid TaskId,
    string Title,
    string Body,
    string LocalDue,
    DateTimeOffset DueAt,
    string TaskUrl,
    string ActionToken);

public interface IReminderChannel
{
    string Name { get; }
    Task<bool> SendAsync(ReminderMessage message, CancellationToken ct);
}
```

`ReminderLinks.cs`:

```csharp
namespace Mindflow.Api.Services.Reminders;

public static class ReminderLinks
{
    public static string FrontendUrl(IConfiguration configuration) =>
        (FirstSet(configuration["Notifications:FrontendUrl"], configuration["Cors:FrontendUrl"]) ?? "").TrimEnd('/');

    public static string TaskUrl(IConfiguration configuration, Guid taskId) =>
        $"{FrontendUrl(configuration)}/zadania/{taskId}";

    public static string ActionApiUrl(IConfiguration configuration) =>
        $"{(configuration["Notifications:PublicApiUrl"] ?? "").TrimEnd('/')}/notifications/reminder-actions";

    public static string ActionPageUrl(IConfiguration configuration, string token) =>
        $"{FrontendUrl(configuration)}/przypomnienie?t={Uri.EscapeDataString(token)}";

    private static string? FirstSet(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
```

`ReminderDispatcher.cs`:

```csharp
namespace Mindflow.Api.Services.Reminders;

public interface IReminderDispatcher
{
    Task<IReadOnlyList<string>> DispatchAsync(ReminderMessage message, CancellationToken ct);
}

public class ReminderDispatcher(IEnumerable<IReminderChannel> channels, ILogger<ReminderDispatcher> logger) : IReminderDispatcher
{
    public async Task<IReadOnlyList<string>> DispatchAsync(ReminderMessage message, CancellationToken ct)
    {
        var delivered = new List<string>();
        foreach (var channel in channels)
        {
            try
            {
                if (await channel.SendAsync(message, ct)) delivered.Add(channel.Name);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Reminder channel {Channel} failed for task {TaskId}.", channel.Name, message.TaskId);
            }
        }

        return delivered;
    }
}
```

`WebPushSender.cs`: przenieś do niego z `NotificationService` metodę `SendToUserAsync` (pętla po subskrypcjach, usuwanie wygasłych 404/410, logi) i `GetVapidDetails`. Sygnatura `Task<bool> SendAsync(Guid userId, object payload, CancellationToken ct)`, serializuje `payload` przez `JsonSerializer.Serialize(payload, JsonSerializerOptions.Web)`. Zależności: `MindflowDbContext db, IConfiguration configuration, ILogger<WebPushSender> logger`. Kolejność jak w oryginale: najpierw subskrypcje (brak = `false`), potem VAPID (brak = `BadRequestException`). W `NotificationService` zostaw prywatne `SendToUserAsync(userId, title, body, tag, targetUrl, ct)` jako jednolinijkowe wywołanie `webPushSender.SendAsync(userId, new { title, body, tag, url = targetUrl }, ct)`. Dodaj `IWebPushSender webPushSender` do konstruktora `NotificationService` i usuń z niego nieużywane już `using WebPush;`/`System.Net`, jeśli przestały być potrzebne.

`NotificationInboxKind.cs`: dopisz `TaskReminder` jako ostatnią wartość.

`ITasksNotifier.cs`: dopisz rekord i metodę:

```csharp
public record ReminderDuePayload(Guid NotificationId, Guid TaskId, string Title, string Body, DateTimeOffset DueAt, string ActionToken);
```
```csharp
    Task ReminderDueAsync(Guid userId, ReminderDuePayload payload);
```

`TasksNotifier.cs`: stała `private const string ReminderDue = "ReminderDue";` i

```csharp
    public System.Threading.Tasks.Task ReminderDueAsync(Guid userId, ReminderDuePayload payload)
        => BroadcastToUserAsync(ReminderDue, userId, payload);
```

Jeśli testy mają własną implementację `ITasksNotifier` (`grep -rn ITasksNotifier src/Mindflow/Mindflow.Api.Tests`), dodaj tam pustą metodę.

`InAppReminderChannel.cs`:

```csharp
using Mindflow.Api.Data;
using Mindflow.Api.Hubs;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Enums;

namespace Mindflow.Api.Services.Reminders;

public class InAppReminderChannel(
    MindflowDbContext db,
    ITasksNotifier notifier,
    ILogger<InAppReminderChannel> logger) : IReminderChannel
{
    public string Name => "app";

    public async Task<bool> SendAsync(ReminderMessage message, CancellationToken ct)
    {
        var item = new NotificationInboxItem
        {
            Id = Guid.NewGuid(),
            UserId = message.UserId,
            Kind = NotificationInboxKind.TaskReminder,
            Title = message.Title,
            Body = message.Body,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.NotificationInboxItems.Add(item);
        await db.SaveChangesAsync(ct);

        try
        {
            await notifier.ReminderDueAsync(message.UserId, new ReminderDuePayload(
                item.Id, message.TaskId, message.Title, message.Body, message.DueAt, message.ActionToken));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "ReminderDue broadcast failed for task {TaskId}.", message.TaskId);
        }

        return true;
    }
}
```

`PushReminderChannel.cs`:

```csharp
namespace Mindflow.Api.Services.Reminders;

public class PushReminderChannel(IWebPushSender sender, IConfiguration configuration) : IReminderChannel
{
    private static readonly int[] VibratePattern = [300, 120, 300, 120, 600];

    public string Name => "push";

    public Task<bool> SendAsync(ReminderMessage message, CancellationToken ct) =>
        sender.SendAsync(message.UserId, new
        {
            kind = "reminder",
            title = message.Title,
            body = message.Body,
            tag = $"reminder:{message.TaskId:N}",
            url = $"/zadania/{message.TaskId}",
            requireInteraction = true,
            renotify = true,
            vibrate = VibratePattern,
            taskId = message.TaskId,
            actionToken = message.ActionToken,
            actionUrl = ReminderLinks.ActionApiUrl(configuration)
        }, ct);
}
```

`ServiceCollectionExtensions.AddMindflowServices`, obok `INotificationService` (dodaj `using Mindflow.Api.Services.Reminders;`):

```csharp
        services.AddScoped<IWebPushSender, WebPushSender>();
        services.AddSingleton<IReminderActionTokens, ReminderActionTokens>();
        services.AddScoped<IReminderChannel, InAppReminderChannel>();
        services.AddScoped<IReminderChannel, PushReminderChannel>();
        services.AddScoped<IReminderDispatcher, ReminderDispatcher>();
```

- [ ] **Step 4: Run tests** — `dotnet test src/Mindflow/Mindflow.sln` z bazą → całość zielona (w tym istniejące testy inboxu).

- [ ] **Step 5: Commit**

```bash
git add -A src/Mindflow
git commit -m "feat: dispatch reminders to app and web push channels"
```

---

### Task 5: Job wysyła przypomnienia zadań

**Files:**
- Create: `src/Mindflow/Mindflow.Api/Services/Reminders/TaskReminderService.cs`
- Modify: `src/Mindflow/Mindflow.Api/Services/NotificationService.cs` (usuń `SendDueTaskRemindersAsync`, wołaj nowy serwis)
- Modify: `src/Mindflow/Mindflow.Api/Extensions/ServiceCollectionExtensions.cs`
- Test: `src/Mindflow/Mindflow.Api.Tests/Reminders/TaskReminderJobTests.cs`

**Interfaces:**
- Consumes: `ReminderSchedule`, `ReminderText`, `ReminderLinks`, `IReminderDispatcher`, `IReminderActionTokens`.
- Produces: `interface ITaskReminderService { Task<int> SendDueAsync(DateTimeOffset now, CancellationToken ct = default); }`

- [ ] **Step 1: Write the failing test**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Services.Reminders;
using Mindflow.Api.Tests.Infrastructure;

namespace Mindflow.Api.Tests.Reminders;

public class TaskReminderJobTests(MindflowApiFactory factory) : IClassFixture<MindflowApiFactory>
{
    private static readonly TimeZoneInfo Warsaw = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");

    [LocalDatabaseFact]
    public async Task Sends_default_reminder_once_to_the_inbox()
    {
        var now = DateTimeOffset.UtcNow;
        var user = await factory.CreateUserAsync();
        await SeedTaskAsync(user.Id, now.AddMinutes(10), "Telefon do księgowej");

        await RunAsync(now);
        await RunAsync(now.AddMinutes(5));

        var items = await InboxAsync(user.Id);
        var item = Assert.Single(items);
        Assert.Equal(NotificationInboxKind.TaskReminder, item.Kind);
        Assert.Equal("⏰ Telefon do księgowej", item.Title);
        Assert.StartsWith("Za 10 min", item.Body);
    }

    [LocalDatabaseFact]
    public async Task Respects_own_minutes_and_disabled_flag()
    {
        var now = DateTimeOffset.UtcNow;
        var user = await factory.CreateUserAsync();
        await SeedTaskAsync(user.Id, now.AddMinutes(60), "Za godzinę", minutes: 60);
        await SeedTaskAsync(user.Id, now.AddMinutes(10), "Wyłączone", disabled: true);

        await RunAsync(now);

        Assert.Equal(["⏰ Za godzinę"], (await InboxAsync(user.Id)).Select(i => i.Title).ToList());
    }

    [LocalDatabaseFact]
    public async Task Sends_snoozed_reminder_again()
    {
        var now = DateTimeOffset.UtcNow;
        var user = await factory.CreateUserAsync();
        var taskId = await SeedTaskAsync(user.Id, now.AddMinutes(10), "Lek");
        await RunAsync(now);

        await factory.WithDbAsync(async db =>
        {
            var task = await db.Tasks.SingleAsync(t => t.Id == taskId);
            task.ReminderSnoozedUntil = now.AddMinutes(10);
            await db.SaveChangesAsync();
        });
        await RunAsync(now.AddMinutes(11));

        var bodies = (await InboxAsync(user.Id)).Select(i => i.Body).ToList();
        Assert.Equal(2, bodies.Count);
        Assert.Contains(bodies, body => body.StartsWith("Odłożone przypomnienie"));
    }

    private async Task RunAsync(DateTimeOffset now)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ITaskReminderService>().SendDueAsync(now);
    }

    private async Task<Guid> SeedTaskAsync(Guid userId, DateTimeOffset dueAtUtc, string content, int? minutes = null, bool disabled = false)
    {
        var local = TimeZoneInfo.ConvertTime(dueAtUtc, Warsaw);
        var task = new TaskItem
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Content = content,
            DueDate = DateOnly.FromDateTime(local.DateTime),
            DueTime = new TimeOnly(local.Hour, local.Minute),
            ReminderMinutesBefore = minutes,
            ReminderDisabled = disabled,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await factory.WithDbAsync(async db =>
        {
            db.Tasks.Add(task);
            await db.SaveChangesAsync();
        });
        return task.Id;
    }

    private Task<List<NotificationInboxItem>> InboxAsync(Guid userId) =>
        factory.WithDbAsync(db => db.NotificationInboxItems
            .Where(i => i.UserId == userId)
            .OrderBy(i => i.CreatedAt)
            .ToListAsync());
}
```

`CreateUserAsync` ustawia `TimeZone = "Europe/Warsaw"`, więc seed liczy godzinę w tej strefie. Godzina terminu jest zaokrąglana w dół do pełnej minuty, więc `RemindAt <= now` jest zawsze spełnione.

- [ ] **Step 2: Run to verify failure** — `--filter TaskReminderJobTests` → błąd kompilacji (`ITaskReminderService`).

- [ ] **Step 3: Implement**

```csharp
using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Data;
using Mindflow.Api.Models;
using TaskStatus = Mindflow.Api.Models.Enums.TaskStatus;

namespace Mindflow.Api.Services.Reminders;

public interface ITaskReminderService
{
    Task<int> SendDueAsync(DateTimeOffset now, CancellationToken ct = default);
}

public class TaskReminderService(
    MindflowDbContext db,
    IReminderDispatcher dispatcher,
    IReminderActionTokens tokens,
    IConfiguration configuration,
    ILogger<TaskReminderService> logger) : ITaskReminderService
{
    private static readonly TimeSpan TokenLifetimeAfterDue = TimeSpan.FromDays(2);

    public async Task<int> SendDueAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var from = today.AddDays(-2);
        var to = today.AddDays(8);
        var tasks = await db.Tasks
            .AsNoTracking()
            .Where(task => !task.IsCompleted
                           && task.Status != TaskStatus.Completed
                           && !task.ReminderDisabled
                           && task.RecurringTaskId == null
                           && task.Recurrence == null
                           && task.DueDate.HasValue
                           && task.DueTime.HasValue
                           && task.DueDate.Value >= from
                           && task.DueDate.Value <= to)
            .ToListAsync(ct);
        if (tasks.Count == 0) return 0;

        var userIds = tasks.Select(task => task.UserId).Distinct().ToArray();
        var timeZones = await db.Users.AsNoTracking()
            .Where(user => userIds.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.TimeZone, ct);
        var settingsByUser = await db.NotificationSettings.AsNoTracking()
            .Where(settings => userIds.Contains(settings.UserId))
            .ToDictionaryAsync(settings => settings.UserId, ct);

        var sent = 0;
        foreach (var task in tasks)
        {
            var settings = settingsByUser.GetValueOrDefault(task.UserId) ?? new NotificationSettings();
            var timeZone = ResolveTimeZone(timeZones.GetValueOrDefault(task.UserId));
            var due = ReminderSchedule.FindDue(task, settings, timeZone, now);
            if (due is null) continue;

            var alreadySent = await db.PushNotificationDeliveries.AsNoTracking()
                .AnyAsync(delivery => delivery.UserId == task.UserId && delivery.DeliveryKey == due.DeliveryKey, ct);
            if (alreadySent) continue;

            var text = ReminderText.Build(task.Content, due, timeZone, now);
            var expiresAt = (due.DueAt > now ? due.DueAt : now) + TokenLifetimeAfterDue;
            var message = new ReminderMessage(
                task.UserId,
                task.Id,
                text.Title,
                text.Body,
                text.LocalDue,
                due.DueAt,
                ReminderLinks.TaskUrl(configuration, task.Id),
                tokens.Create(task.Id, task.UserId, expiresAt));

            var delivered = await dispatcher.DispatchAsync(message, ct);
            if (delivered.Count == 0) continue;

            if (await RecordDeliveryAsync(task.UserId, due.DeliveryKey, now, ct)) sent++;
        }

        return sent;
    }

    private async Task<bool> RecordDeliveryAsync(Guid userId, string deliveryKey, DateTimeOffset now, CancellationToken ct)
    {
        var delivery = new PushNotificationDelivery
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            DeliveryKey = deliveryKey,
            SentAt = now
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

    private TimeZoneInfo ResolveTimeZone(string? timeZone)
    {
        if (string.IsNullOrWhiteSpace(timeZone)) return TimeZoneInfo.Utc;
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZone);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            logger.LogWarning("Unknown time zone {TimeZone}; using UTC for reminders.", timeZone);
            return TimeZoneInfo.Utc;
        }
    }
}
```

`NotificationService`:
- dodaj `ITaskReminderService taskReminderService` do konstruktora;
- w `ProcessDueNotificationsAsync` usuń wywołanie `SendDueTaskRemindersAsync(...)` z pętli po użytkownikach i usuń całą metodę `SendDueTaskRemindersAsync`;
- po pętli, przed `SendDueBlockRemindersAsync`, dodaj `taskRemindersSent += await taskReminderService.SendDueAsync(now, ct);`.

DI: `services.AddScoped<ITaskReminderService, TaskReminderService>();` obok rejestracji z Task 4.

- [ ] **Step 4: Run tests** — pełne `dotnet test src/Mindflow/Mindflow.sln` z bazą → zielone.

- [ ] **Step 5: Commit**

```bash
git add -A src/Mindflow
git commit -m "feat: send task reminders from the notification job"
```

---

### Task 6: Akcje Zrobione / Odłóż

**Files:**
- Create: `src/Mindflow/Mindflow.Api/Services/Reminders/ReminderActionService.cs`
- Create: `src/Mindflow/Mindflow.Api/Controllers/ReminderActionsController.cs`
- Modify: `src/Mindflow/Mindflow.Api/Models/Dtos/NotificationDtos.cs`
- Modify: `src/Mindflow/Mindflow.Api/Extensions/ServiceCollectionExtensions.cs`
- Test: `src/Mindflow/Mindflow.Api.Tests/Reminders/ReminderActionsApiTests.cs`

**Interfaces:**
- Consumes: `IReminderActionTokens`, `ITaskService.CompleteAsync(Guid, CompleteTaskRequest)`.
- Produces:
  - `enum ReminderAction { Done, Snooze }`, `ReminderActionService.SnoozeMinutes = 10`
  - `interface IReminderActionService { Task<ReminderActionResponse?> ApplyAsync(Guid userId, Guid taskId, ReminderAction action, CancellationToken ct); }` (null = zadanie nie istnieje albo nie należy do użytkownika). Telegram (etap 4) użyje tej samej metody.
  - `record ReminderActionRequest([Required, StringLength(512)] string Token, [Required, StringLength(16)] string Action)`, `record ReminderActionResponse(string Action, Guid TaskId, string TaskTitle, DateTimeOffset? SnoozedUntil)`
  - `POST /notifications/reminder-actions` → 200 `ReminderActionResponse` / 400 / 404

- [ ] **Step 1: Write the failing test**

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Services.Reminders;
using Mindflow.Api.Tests.Infrastructure;
using TaskStatus = Mindflow.Api.Models.Enums.TaskStatus;

namespace Mindflow.Api.Tests.Reminders;

public class ReminderActionsApiTests(MindflowApiFactory factory) : IClassFixture<MindflowApiFactory>
{
    private const string Url = "/notifications/reminder-actions";

    [LocalDatabaseFact]
    public async Task Snooze_moves_reminder_ten_minutes_ahead()
    {
        var (userId, taskId) = await SeedAsync();
        var before = DateTimeOffset.UtcNow;

        var response = await factory.CreateClient().PostAsJsonAsync(Url, new { token = Token(taskId, userId), action = "snooze" });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ReminderActionResponse>(MindflowApiFactory.Json);
        Assert.Equal(("snooze", "Telefon"), (body!.Action, body.TaskTitle));
        var snoozed = await factory.WithDbAsync(db => db.Tasks.Where(t => t.Id == taskId).Select(t => t.ReminderSnoozedUntil).SingleAsync());
        Assert.InRange(snoozed!.Value, before.AddMinutes(10), DateTimeOffset.UtcNow.AddMinutes(10));
    }

    [LocalDatabaseFact]
    public async Task Done_completes_the_task()
    {
        var (userId, taskId) = await SeedAsync();

        var response = await factory.CreateClient().PostAsJsonAsync(Url, new { token = Token(taskId, userId), action = "done" });

        response.EnsureSuccessStatusCode();
        var task = await factory.WithDbAsync(db => db.Tasks.SingleAsync(t => t.Id == taskId));
        Assert.True(task.IsCompleted);
        Assert.Equal(TaskStatus.Completed, task.Status);
    }

    [LocalDatabaseFact]
    public async Task Rejects_bad_token_and_unknown_action()
    {
        var (userId, taskId) = await SeedAsync();
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Url, new { token = "garbage", action = "done" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Url, new { token = Token(taskId, userId), action = "delete" })).StatusCode);
    }

    [LocalDatabaseFact]
    public async Task Token_of_another_user_cannot_touch_the_task()
    {
        var (_, taskId) = await SeedAsync();
        var stranger = await factory.CreateUserAsync();

        var response = await factory.CreateClient().PostAsJsonAsync(Url, new { token = Token(taskId, stranger.Id), action = "done" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(await factory.WithDbAsync(db => db.Tasks.Where(t => t.Id == taskId).Select(t => t.IsCompleted).SingleAsync()));
    }

    private string Token(Guid taskId, Guid userId) =>
        factory.Services.GetRequiredService<IReminderActionTokens>().Create(taskId, userId, DateTimeOffset.UtcNow.AddDays(1));

    private async Task<(Guid UserId, Guid TaskId)> SeedAsync()
    {
        var user = await factory.CreateUserAsync();
        var task = new TaskItem
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Content = "Telefon",
            DueDate = new DateOnly(2030, 1, 10),
            DueTime = new TimeOnly(14, 0),
            CreatedAt = DateTimeOffset.UtcNow
        };
        await factory.WithDbAsync(async db =>
        {
            db.Tasks.Add(task);
            await db.SaveChangesAsync();
        });
        return (user.Id, task.Id);
    }
}
```

- [ ] **Step 2: Run to verify failure** — `--filter ReminderActionsApiTests` → błąd kompilacji.

- [ ] **Step 3: Implement**

`NotificationDtos.cs`, na końcu:

```csharp
public record ReminderActionRequest(
    [Required, StringLength(512)] string Token,
    [Required, StringLength(16)] string Action);

public record ReminderActionResponse(string Action, Guid TaskId, string TaskTitle, DateTimeOffset? SnoozedUntil);
```

`ReminderActionService.cs`. „Zrobione” idzie przez `ITaskService.CompleteAsync`, żeby aktywność i SignalR działały jak przy zwykłym zamknięciu. `TaskService` bierze użytkownika z `HttpContext.User`, więc serwis ustawia tożsamość właściciela potwierdzoną tokenem albo czatem Telegrama:

```csharp
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Data;
using Mindflow.Api.Models.Dtos;

namespace Mindflow.Api.Services.Reminders;

public enum ReminderAction
{
    Done,
    Snooze
}

public interface IReminderActionService
{
    Task<ReminderActionResponse?> ApplyAsync(Guid userId, Guid taskId, ReminderAction action, CancellationToken ct);
}

public class ReminderActionService(
    MindflowDbContext db,
    ITaskService taskService,
    IHttpContextAccessor httpContextAccessor) : IReminderActionService
{
    public const int SnoozeMinutes = 10;

    public async Task<ReminderActionResponse?> ApplyAsync(Guid userId, Guid taskId, ReminderAction action, CancellationToken ct)
    {
        var task = await db.Tasks.SingleOrDefaultAsync(t => t.Id == taskId && t.UserId == userId, ct);
        if (task is null) return null;

        if (action == ReminderAction.Snooze)
        {
            task.ReminderSnoozedUntil = DateTimeOffset.UtcNow.AddMinutes(SnoozeMinutes);
            await db.SaveChangesAsync(ct);
            return new ReminderActionResponse("snooze", task.Id, task.Content, task.ReminderSnoozedUntil);
        }

        if (!task.IsCompleted)
        {
            ActAs(userId);
            await taskService.CompleteAsync(task.Id, new CompleteTaskRequest(null, false, null, null, null, null, null));
        }

        return new ReminderActionResponse("done", task.Id, task.Content, null);
    }

    private void ActAs(Guid userId)
    {
        var context = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("Reminder actions require an HTTP request.");
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "ReminderAction"));
    }
}
```

`ReminderActionsController.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Services.Reminders;

namespace Mindflow.Api.Controllers;

[ApiController]
[Route("notifications/reminder-actions")]
[AllowAnonymous]
public class ReminderActionsController(
    IReminderActionTokens tokens,
    IReminderActionService actions) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ReminderActionResponse>> Apply(ReminderActionRequest request, CancellationToken ct)
    {
        var claims = tokens.Validate(request.Token, DateTimeOffset.UtcNow);
        if (claims is null) return BadRequest(new { message = "Invalid or expired reminder link." });

        ReminderAction? action = request.Action.ToLowerInvariant() switch
        {
            "done" => ReminderAction.Done,
            "snooze" => ReminderAction.Snooze,
            _ => null
        };
        if (action is null) return BadRequest(new { message = "Unknown reminder action." });

        var result = await actions.ApplyAsync(claims.UserId, claims.TaskId, action.Value, ct);
        return result is null ? NotFound() : Ok(result);
    }
}
```

Sprawdź, czy `AddHttpContextAccessor()` jest już zarejestrowany (`CurrentUserService` go używa, więc powinien być). DI: `services.AddScoped<IReminderActionService, ReminderActionService>();`.

Jeśli `CompleteAsync` przez `IAccessService` albo `IIntegrationSpaceScope` wymaga czegoś więcej niż claim `NameIdentifier` (sprawdź `AccessService.CanAccessTaskAsync`), dopasuj tożsamość w `ActAs` tak, jak buduje ją zwykłe logowanie JWT (`AddMindflowAuth`).

- [ ] **Step 4: Run tests** — pełne `dotnet test` z bazą → zielone.

- [ ] **Step 5: Commit**

```bash
git add -A src/Mindflow
git commit -m "feat: complete or snooze a task from a reminder"
```

---

### Task 7: Dokumentacja konfiguracji

**Files:**
- Modify: `docs/push-notifications.md`

- [ ] **Step 1:** Dopisz sekcję „Przypomnienia o zadaniach”: pole przy zadaniu (`reminderMinutesBefore`, `reminderDisabled`, wartość domyślna z `BlockReminderMinutes`), okno 15 min, akcje `POST /notifications/reminder-actions`, zdarzenie SignalR `ReminderDue`, zmienne `Notifications__PublicApiUrl` i `Notifications__FrontendUrl` oraz migracja `AddTaskReminders`. Krótko, po polsku, w stylu pliku.
- [ ] **Step 2: Commit** — `git commit -am "docs: describe task reminders"`
