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
    public async Task Done_works_outside_an_http_request()
    {
        var (userId, taskId) = await SeedAsync();

        using var scope = factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IReminderActionService>()
            .ApplyAsync(userId, taskId, ReminderAction.Done, CancellationToken.None);

        Assert.Equal("done", result!.Action);
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
