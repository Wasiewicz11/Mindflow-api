# Reminder Email (API, etap 3) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use subagent-driven-development (recommended) or executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Przypomnienie o zadaniu przychodzi także mailem (SMTP), jeśli użytkownik ma włączony przełącznik „E-mail” (domyślnie włączony), a serwer ma skonfigurowany SMTP.

**Architecture:** Nowy `IReminderChannel` (`EmailReminderChannel`) wpięty do istniejącego `ReminderDispatcher` z etapu 1. Treść buduje czysta klasa `ReminderEmail`, wysyła `IEmailSender` (MailKit). Przełącznik `EmailRemindersEnabled` siedzi w `NotificationSettings`.

**Tech Stack:** .NET 10, MailKit (SMTP), EF Core, xUnit + WebApplicationFactory.

**Spec:** `/Users/filipwasiewicz/Projekty/Mindflow/mindflow-web-reminders/docs/superpowers/specs/2026-11-09-przypomnienia-design.md`

## Global Constraints

- Worktree `/Users/filipwasiewicz/Projekty/Mindflow/mindflow-api-reminders`, gałąź `feature/reminder-email`, utworzona od `feature/task-reminders` (etap 1, jeszcze niezmergowany).
- Commity: Conventional Commits po angielsku, małą literą, tryb rozkazujący, bez opisu, bez `Co-Authored-By`, bez wzmianek o AI. Test w tym samym commicie co kod.
- W kodzie zero komentarzy „dlaczego”.
- Konfiguracja: `Email:Smtp:Host`, `Email:Smtp:Port` (domyślnie 587), `Email:Smtp:User`, `Email:Smtp:Password`, `Email:Smtp:From` (np. `Mindflow <powiadomienia@domena>`), `Email:Smtp:UseSsl` (domyślnie `true`, a dla portu 465 SSL on connect, w pozostałych przypadkach StartTls). Bez `Host` albo `From` kanał nic nie wysyła (zwraca `false`) i nie rzuca wyjątku.
- Temat maila: `⏰ {tytuł zadania} o {HH:mm}` (godzina lokalna terminu, maks. 120 znaków tytułu). Przyciski prowadzą do `ReminderLinks.ActionPageUrl(config, token)` z dopiskiem `&a=done` / `&a=snooze` oraz do `message.TaskUrl` („Otwórz zadanie”).
- `PUT /notifications/settings`: nowe pole opcjonalne `emailRemindersEnabled` (`null` = bez zmian). Odpowiedź zawiera `emailRemindersEnabled` i `emailConfigured` (czy serwer ma SMTP).
- Testy DB: `ConnectionStrings__Database='Host=localhost;Port=5435;Database=mindflow_reminders_tests;Username=postgres;Password=postgres'`. Fabryka nie migruje, więc po migracji uruchom `dotnet ef database update` z tym env.

---

### Task 1: Przełącznik e-mail w ustawieniach

**Files:**
- Modify: `src/Mindflow/Mindflow.Api/Models/NotificationSettings.cs` (`public bool EmailRemindersEnabled { get; set; } = true;`)
- Modify: `src/Mindflow/Mindflow.Api/Models/Dtos/NotificationDtos.cs`
- Modify: `src/Mindflow/Mindflow.Api/Services/NotificationService.cs` (`UpdateSettingsAsync`, `ToResponse`)
- Create (generated): migracja `AddEmailReminderSetting` (kolumna `email_reminders_enabled boolean NOT NULL DEFAULT true`). Sprawdź w wygenerowanym pliku, że `defaultValue: true` (a nie `false`), żeby istniejące wiersze dostały `true`. Jeśli EF wygeneruje `false`, popraw `defaultValue` w migracji ręcznie.
- Test: `src/Mindflow/Mindflow.Api.Tests/Reminders/NotificationSettingsEmailApiTests.cs`

**Interfaces:**
- Produces: `NotificationSettingsResponse` z dopisanymi na końcu `bool EmailRemindersEnabled, bool EmailConfigured`; `UpdateNotificationSettingsRequest` z dopisanym na końcu `bool? EmailRemindersEnabled = null`; statyczne `EmailOptions.IsConfigured(IConfiguration)` w `Services/Email/EmailOptions.cs` (Host i From niepuste).

- [ ] **Step 1: Write the failing test**

```csharp
using System.Net.Http.Json;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Tests.Infrastructure;

namespace Mindflow.Api.Tests.Reminders;

public class NotificationSettingsEmailApiTests(MindflowApiFactory factory) : IClassFixture<MindflowApiFactory>
{
    [LocalDatabaseFact]
    public async Task Email_reminders_are_on_by_default_and_can_be_switched_off()
    {
        var user = await factory.CreateUserAsync();
        var client = factory.CreateUserClient(user.Id);

        var initial = await client.GetFromJsonAsync<NotificationSettingsResponse>("/notifications/settings", MindflowApiFactory.Json);
        Assert.True(initial!.EmailRemindersEnabled);
        Assert.False(initial.EmailConfigured);

        var response = await client.PutAsJsonAsync("/notifications/settings", Body(initial, emailRemindersEnabled: false));
        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<NotificationSettingsResponse>(MindflowApiFactory.Json);
        Assert.False(updated!.EmailRemindersEnabled);
    }

    [LocalDatabaseFact]
    public async Task Old_clients_without_the_field_keep_the_value()
    {
        var user = await factory.CreateUserAsync();
        var client = factory.CreateUserClient(user.Id);
        var initial = await client.GetFromJsonAsync<NotificationSettingsResponse>("/notifications/settings", MindflowApiFactory.Json);
        await client.PutAsJsonAsync("/notifications/settings", Body(initial!, emailRemindersEnabled: false));

        var response = await client.PutAsJsonAsync("/notifications/settings", Body(initial!, emailRemindersEnabled: null));

        var updated = await response.Content.ReadFromJsonAsync<NotificationSettingsResponse>(MindflowApiFactory.Json);
        Assert.False(updated!.EmailRemindersEnabled);
    }

    private static object Body(NotificationSettingsResponse s, bool? emailRemindersEnabled) => new
    {
        s.Enabled, s.MorningBriefEnabled, s.MorningBriefTime, s.MiddayBriefEnabled, s.MiddayBriefTime,
        s.EveningSummaryEnabled, s.EveningSummaryTime, s.BlockRemindersEnabled, s.BlockReminderMinutes,
        emailRemindersEnabled
    };
}
```

- [ ] **Step 2: Run** `--filter NotificationSettingsEmailApiTests` → błąd kompilacji.
- [ ] **Step 3: Implement.** Pole w modelu. DTO (patrz Interfaces). W `UpdateSettingsAsync`: `if (request.EmailRemindersEnabled.HasValue) settings.EmailRemindersEnabled = request.EmailRemindersEnabled.Value;`. `ToResponse` dostaje `bool emailConfigured` (z `EmailOptions.IsConfigured(configuration)`). `EmailOptions.cs`:

```csharp
namespace Mindflow.Api.Services.Email;

public record EmailOptions(string Host, int Port, string? User, string? Password, string From, bool UseSsl)
{
    public static bool IsConfigured(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration["Email:Smtp:Host"])
        && !string.IsNullOrWhiteSpace(configuration["Email:Smtp:From"]);

    public static EmailOptions? From(IConfiguration configuration) => IsConfigured(configuration)
        ? new EmailOptions(
            configuration["Email:Smtp:Host"]!,
            int.TryParse(configuration["Email:Smtp:Port"], out var port) ? port : 587,
            configuration["Email:Smtp:User"],
            configuration["Email:Smtp:Password"],
            configuration["Email:Smtp:From"]!,
            !bool.TryParse(configuration["Email:Smtp:UseSsl"], out var useSsl) || useSsl)
        : null;
}
```

Migracja: `dotnet ef migrations add AddEmailReminderSetting --project src/Mindflow/Mindflow.Api/Mindflow.Api.csproj --startup-project src/Mindflow/Mindflow.Api/Mindflow.Api.csproj`, potem `database update`.
- [ ] **Step 4: Run** pełne `dotnet test src/Mindflow/Mindflow.sln` z DB → zielone.
- [ ] **Step 5: Commit** `feat: add email reminder setting`

---

### Task 2: Kanał e-mail

**Files:**
- Modify: `src/Mindflow/Mindflow.Api/Mindflow.Api.csproj` (`dotnet add src/Mindflow/Mindflow.Api package MailKit`, wersja stabilna, którą wybierze NuGet)
- Create: `src/Mindflow/Mindflow.Api/Services/Email/IEmailSender.cs` (`record EmailMessage(string To, string Subject, string Html, string Text)`, `interface IEmailSender { Task SendAsync(EmailMessage message, CancellationToken ct); }`)
- Create: `src/Mindflow/Mindflow.Api/Services/Email/SmtpEmailSender.cs`
- Create: `src/Mindflow/Mindflow.Api/Services/Reminders/ReminderEmail.cs`
- Create: `src/Mindflow/Mindflow.Api/Services/Reminders/EmailReminderChannel.cs`
- Modify: `src/Mindflow/Mindflow.Api/Extensions/ServiceCollectionExtensions.cs`
- Test: `src/Mindflow/Mindflow.Api.Tests/Reminders/ReminderEmailTests.cs`, `src/Mindflow/Mindflow.Api.Tests/Reminders/EmailReminderChannelTests.cs`

**Interfaces:**
- Consumes: `ReminderMessage(NotificationId, UserId, TaskId, Title, Body, LocalDue, DueAt, TaskUrl, ActionToken)`, `IReminderChannel`, `ReminderLinks.ActionPageUrl(IConfiguration, string token)` (etap 1). `Title` ma postać `⏰ {tytuł}`, a `LocalDue` np. `dziś 14:00`.
- Produces: `ReminderEmail.Build(ReminderMessage message, string actionPageUrl, string to) : EmailMessage`; `EmailReminderChannel` (`Name = "email"`).

- [ ] **Step 1: Write the failing tests**

```csharp
using Mindflow.Api.Services.Reminders;

namespace Mindflow.Api.Tests.Reminders;

public class ReminderEmailTests
{
    private static readonly ReminderMessage Message = new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "⏰ Telefon <do> księgowej", "Za 10 min · termin dziś 14:00", "dziś 14:00",
        DateTimeOffset.UtcNow, "https://app.test/zadania/1", "tok");

    [Fact]
    public void Subject_has_title_and_time()
    {
        var email = ReminderEmail.Build(Message, "https://app.test/przypomnienie?t=tok", "a@b.pl");
        Assert.Equal("⏰ Telefon <do> księgowej o 14:00", email.Subject);
        Assert.Equal("a@b.pl", email.To);
    }

    [Fact]
    public void Html_escapes_title_and_links_to_actions()
    {
        var email = ReminderEmail.Build(Message, "https://app.test/przypomnienie?t=tok", "a@b.pl");
        Assert.Contains("Telefon &lt;do&gt; księgowej", email.Html);
        Assert.DoesNotContain("<do>", email.Html);
        Assert.Contains("https://app.test/przypomnienie?t=tok&amp;a=done", email.Html);
        Assert.Contains("https://app.test/przypomnienie?t=tok&amp;a=snooze", email.Html);
        Assert.Contains("https://app.test/zadania/1", email.Html);
        Assert.Contains("Za 10 min · termin dziś 14:00", email.Text);
        Assert.Contains("https://app.test/przypomnienie?t=tok&a=done", email.Text);
    }
}
```

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Mindflow.Api.Models;
using Mindflow.Api.Services.Email;
using Mindflow.Api.Services.Reminders;
using Mindflow.Api.Tests.Infrastructure;

namespace Mindflow.Api.Tests.Reminders;

public class EmailReminderChannelTests(MindflowApiFactory factory) : IClassFixture<MindflowApiFactory>
{
    private sealed class RecordingSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];
        public Task SendAsync(EmailMessage message, CancellationToken ct) { Sent.Add(message); return Task.CompletedTask; }
    }

    private (IServiceProvider Services, RecordingSender Sender) Configured(bool smtp = true)
    {
        var sender = new RecordingSender();
        var app = factory.WithWebHostBuilder(builder =>
        {
            if (smtp)
            {
                builder.UseSetting("Email:Smtp:Host", "smtp.test");
                builder.UseSetting("Email:Smtp:From", "Mindflow <noreply@mindflow.test>");
            }
            builder.UseSetting("Notifications:FrontendUrl", "https://app.test");
            builder.ConfigureTestServices(services => services.AddSingleton<IEmailSender>(sender));
        });
        return (app.Services, sender);
    }

    private static ReminderMessage MessageFor(Guid userId) => new(
        Guid.NewGuid(), userId, Guid.NewGuid(), "⏰ Lek", "Teraz · termin dziś 08:00", "dziś 08:00", DateTimeOffset.UtcNow, "https://app.test/zadania/x", "tok");

    private static async Task<bool> SendAsync(IServiceProvider services, ReminderMessage message)
    {
        using var scope = services.CreateScope();
        var channel = scope.ServiceProvider.GetServices<IReminderChannel>().Single(c => c.Name == "email");
        return await channel.SendAsync(message, CancellationToken.None);
    }

    [LocalDatabaseFact]
    public async Task Sends_to_the_users_address_when_enabled()
    {
        var user = await factory.CreateUserAsync();
        var (services, sender) = Configured();

        Assert.True(await SendAsync(services, MessageFor(user.Id)));

        var email = Assert.Single(sender.Sent);
        Assert.Equal(user.Email, email.To);
        Assert.Contains("https://app.test/przypomnienie?t=tok", email.Text);
    }

    [LocalDatabaseFact]
    public async Task Skips_when_user_turned_email_off_or_smtp_missing()
    {
        var user = await factory.CreateUserAsync();
        await factory.WithDbAsync(async db =>
        {
            db.NotificationSettings.Add(new NotificationSettings { UserId = user.Id, EmailRemindersEnabled = false, UpdatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        });
        var (services, sender) = Configured();
        Assert.False(await SendAsync(services, MessageFor(user.Id)));

        var other = await factory.CreateUserAsync();
        var (unconfigured, otherSender) = Configured(smtp: false);
        Assert.False(await SendAsync(unconfigured, MessageFor(other.Id)));

        Assert.Empty(sender.Sent);
        Assert.Empty(otherSender.Sent);
    }
}
```

Jeśli `WithWebHostBuilder` na `MindflowApiFactory` nie przenosi ustawień bazowych (sprawdź `ConfigureWebHost`), utwórz w teście podklasę fabryki z nadpisanym `Settings` (wzór: `Infrastructure/AiApiFactory.cs`) i podmień `IEmailSender` w `ConfigureWebHost` przez `ConfigureTestServices`.

- [ ] **Step 2: Run** → błąd kompilacji.
- [ ] **Step 3: Implement**

`ReminderEmail.cs`. Z `LocalDue` bierz godzinę (ostatnie 5 znaków, `HH:mm`), a z `Title` zdejmij prefiks `⏰ `. Wszystko, co wstawiasz do HTML, przechodzi przez `System.Net.WebUtility.HtmlEncode`.

```csharp
using System.Net;
using Mindflow.Api.Services.Email;

namespace Mindflow.Api.Services.Reminders;

public static class ReminderEmail
{
    public static EmailMessage Build(ReminderMessage message, string actionPageUrl, string to)
    {
        var taskTitle = message.Title.StartsWith("⏰ ") ? message.Title[2..] : message.Title;
        var time = message.LocalDue.Length >= 5 ? message.LocalDue[^5..] : message.LocalDue;
        var done = $"{actionPageUrl}&a=done";
        var snooze = $"{actionPageUrl}&a=snooze";

        var html = $"""
            <!doctype html>
            <html lang="pl"><body style="margin:0;padding:24px;background:#f4f4f5;font-family:-apple-system,Segoe UI,Roboto,Arial,sans-serif;color:#111">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr><td align="center">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:480px;background:#fff;border-radius:16px;border:2px solid #111;padding:28px">
            <tr><td style="font-size:13px;letter-spacing:.08em;text-transform:uppercase;color:#b91c1c;font-weight:700">⏰ Przypomnienie</td></tr>
            <tr><td style="padding-top:10px;font-size:24px;line-height:1.25;font-weight:700">{WebUtility.HtmlEncode(taskTitle)}</td></tr>
            <tr><td style="padding-top:14px;font-size:40px;font-weight:800;letter-spacing:-.02em">{WebUtility.HtmlEncode(time)}</td></tr>
            <tr><td style="padding-top:4px;font-size:15px;color:#52525b">{WebUtility.HtmlEncode(message.Body)}</td></tr>
            <tr><td style="padding-top:24px">
              <a href="{WebUtility.HtmlEncode(done)}" style="display:inline-block;background:#111;color:#fff;text-decoration:none;font-weight:700;padding:12px 18px;border-radius:10px;margin:0 8px 8px 0">Zrobione</a>
              <a href="{WebUtility.HtmlEncode(snooze)}" style="display:inline-block;background:#fff;color:#111;text-decoration:none;font-weight:700;padding:10px 16px;border-radius:10px;border:2px solid #111;margin:0 8px 8px 0">Odłóż 10 min</a>
              <a href="{WebUtility.HtmlEncode(message.TaskUrl)}" style="display:inline-block;color:#111;font-weight:600;padding:12px 4px">Otwórz zadanie →</a>
            </td></tr>
            </table></td></tr></table></body></html>
            """;
        var text = $"""
            ⏰ Przypomnienie: {taskTitle}
            {message.Body}

            Zrobione: {done}
            Odłóż 10 min: {snooze}
            Otwórz zadanie: {message.TaskUrl}
            """;

        return new EmailMessage(to, $"⏰ {taskTitle} o {time}", html, text);
    }
}
```

`SmtpEmailSender.cs` (MailKit):

```csharp
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Mindflow.Api.Services.Email;

public class SmtpEmailSender(IConfiguration configuration) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        var options = EmailOptions.From(configuration)
            ?? throw new InvalidOperationException("SMTP is not configured.");
        var mime = new MimeMessage();
        mime.From.Add(MailboxAddress.Parse(options.From));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { HtmlBody = message.Html, TextBody = message.Text }.ToMessageBody();

        using var client = new SmtpClient();
        var security = !options.UseSsl
            ? SecureSocketOptions.None
            : options.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
        await client.ConnectAsync(options.Host, options.Port, security, ct);
        if (!string.IsNullOrWhiteSpace(options.User))
            await client.AuthenticateAsync(options.User, options.Password ?? "", ct);
        await client.SendAsync(mime, ct);
        await client.DisconnectAsync(true, ct);
    }
}
```

`EmailReminderChannel.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Data;
using Mindflow.Api.Services.Email;

namespace Mindflow.Api.Services.Reminders;

public class EmailReminderChannel(MindflowDbContext db, IEmailSender sender, IConfiguration configuration) : IReminderChannel
{
    public string Name => "email";

    public async Task<bool> SendAsync(ReminderMessage message, CancellationToken ct)
    {
        if (!EmailOptions.IsConfigured(configuration)) return false;

        var enabled = await db.NotificationSettings.AsNoTracking()
            .Where(settings => settings.UserId == message.UserId)
            .Select(settings => (bool?)settings.EmailRemindersEnabled)
            .SingleOrDefaultAsync(ct) ?? true;
        if (!enabled) return false;

        var email = await db.Users.AsNoTracking()
            .Where(user => user.Id == message.UserId)
            .Select(user => user.Email)
            .SingleOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(email)) return false;

        await sender.SendAsync(ReminderEmail.Build(message, ReminderLinks.ActionPageUrl(configuration, message.ActionToken), email), ct);
        return true;
    }
}
```

DI (`AddMindflowServices`, obok kanałów z etapu 1): `services.AddScoped<IEmailSender, SmtpEmailSender>(); services.AddScoped<IReminderChannel, EmailReminderChannel>();`.

- [ ] **Step 4: Run** pełne testy z DB → zielone.
- [ ] **Step 5: Commit** `feat: send task reminders by email`

---

### Task 3: Dokumentacja i ręczny test z Mailpitem

- [ ] Dopisz w `docs/push-notifications.md` sekcję „E-mail”: zmienne `Email__Smtp__Host/Port/User/Password/From/UseSsl` (przykład dla Gmaila: `smtp.gmail.com`, 587, hasło aplikacji) i lokalny test: `docker run -d --name mindflow-mailpit -p 1025:1025 -p 8025:8025 axllent/mailpit` + `Email__Smtp__Host=localhost Email__Smtp__Port=1025 Email__Smtp__UseSsl=false Email__Smtp__From='Mindflow <noreply@mindflow.local>'`, skrzynka pod `http://localhost:8025`.
- [ ] Commit `docs: describe email reminders`
