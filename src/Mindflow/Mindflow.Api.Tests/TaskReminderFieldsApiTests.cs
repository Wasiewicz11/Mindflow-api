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
