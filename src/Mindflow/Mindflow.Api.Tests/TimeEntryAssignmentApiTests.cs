using System.Net;
using System.Net.Http.Json;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Tests.Infrastructure;

namespace Mindflow.Api.Tests;

public class TimeEntryAssignmentApiTests(MindflowApiFactory factory) : IClassFixture<MindflowApiFactory>
{
    [LocalDatabaseFact]
    public async Task Moves_entry_to_another_task_and_updates_both_totals()
    {
        var (client, userId) = await CreateClientAsync();
        var project = await factory.CreateProjectAsync(userId, "TMS");
        var from = await CreateTaskAsync(client, new { content = "Raport" });
        var to = await CreateTaskAsync(client, new { content = "Spotkanie", projectId = project.Id });
        var entry = await LogAsync(client, from.Id, 90);

        var response = await client.PutAsJsonAsync($"/time-entries/{entry.Id}", new { taskId = to.Id });
        response.EnsureSuccessStatusCode();
        var moved = (await response.Content.ReadFromJsonAsync<UpdateTaskTimeEntryResponse>(MindflowApiFactory.Json))!;

        Assert.Equal(to.Id, moved.TimeEntry.TaskId);
        Assert.Equal(project.Id, moved.TimeEntry.ProjectId);
        Assert.Equal("Spotkanie", moved.TimeEntry.TaskContent);
        Assert.Equal(90, moved.TimeEntry.DurationMinutes);
        Assert.Equal(90, moved.Task!.LoggedMinutes);
        Assert.Equal(0, (await GetTaskAsync(client, from.Id)).LoggedMinutes);
    }

    [LocalDatabaseFact]
    public async Task Detaches_entry_and_sets_its_content_and_project()
    {
        var (client, userId) = await CreateClientAsync();
        var project = await factory.CreateProjectAsync(userId, "Dom");
        var task = await CreateTaskAsync(client, new { content = "Raport" });
        var entry = await LogAsync(client, task.Id, 30);

        var response = await client.PutAsJsonAsync($"/time-entries/{entry.Id}", new { detachTask = true, content = "Zakupy", projectId = project.Id });
        response.EnsureSuccessStatusCode();
        var detached = (await response.Content.ReadFromJsonAsync<UpdateTaskTimeEntryResponse>(MindflowApiFactory.Json))!;

        Assert.Null(detached.TimeEntry.TaskId);
        Assert.Equal("Zakupy", detached.TimeEntry.TaskContent);
        Assert.Equal(project.Id, detached.TimeEntry.ProjectId);
        Assert.Null(detached.Task);

        var cleared = await client.PutAsJsonAsync($"/time-entries/{entry.Id}", new { clearProject = true });
        cleared.EnsureSuccessStatusCode();
        Assert.Null((await cleared.Content.ReadFromJsonAsync<UpdateTaskTimeEntryResponse>(MindflowApiFactory.Json))!.TimeEntry.ProjectId);
    }

    [LocalDatabaseFact]
    public async Task Rejects_project_change_on_entry_logged_against_a_task()
    {
        var (client, userId) = await CreateClientAsync();
        var project = await factory.CreateProjectAsync(userId, "TMS");
        var task = await CreateTaskAsync(client, new { content = "Raport" });
        var entry = await LogAsync(client, task.Id, 30);

        var response = await client.PutAsJsonAsync($"/time-entries/{entry.Id}", new { projectId = project.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [LocalDatabaseFact]
    public async Task Does_not_move_entry_to_someone_elses_task()
    {
        var (client, _) = await CreateClientAsync();
        var (stranger, _) = await CreateClientAsync();
        var task = await CreateTaskAsync(client, new { content = "Raport" });
        var foreign = await CreateTaskAsync(stranger, new { content = "Cudze" });
        var entry = await LogAsync(client, task.Id, 30);

        var response = await client.PutAsJsonAsync($"/time-entries/{entry.Id}", new { taskId = foreign.Id });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(30, (await GetTaskAsync(client, task.Id)).LoggedMinutes);
    }

    private async Task<(HttpClient Client, Guid UserId)> CreateClientAsync()
    {
        var user = await factory.CreateUserAsync();
        return (factory.CreateUserClient(user.Id), user.Id);
    }

    private static async Task<TaskDetailResponse> CreateTaskAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/tasks", body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TaskDetailResponse>(MindflowApiFactory.Json))!;
    }

    private static async Task<TaskDetailResponse> GetTaskAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<TaskDetailResponse>($"/tasks/{id}", MindflowApiFactory.Json))!;

    private static async Task<TaskTimeEntryResponse> LogAsync(HttpClient client, Guid taskId, int minutes)
    {
        var response = await client.PostAsJsonAsync($"/tasks/{taskId}/time-entries", new { workDate = "2030-01-10", durationMinutes = minutes });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TaskTimeEntryMutationResponse>(MindflowApiFactory.Json))!.TimeEntry;
    }
}
