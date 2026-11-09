using System.Net;
using System.Net.Http.Json;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Tests.Infrastructure;
using TaskStatus = Mindflow.Api.Models.Enums.TaskStatus;

namespace Mindflow.Api.Tests;

public class IntegrationTaskStatusApiTests(MindflowApiFactory factory) : IClassFixture<MindflowApiFactory>
{
    private const string Tasks = "/api/integration/v1/tasks";

    [LocalDatabaseFact]
    public async Task Patch_with_is_completed_closes_and_reopens_the_task()
    {
        using var client = await CreateClientAsync();
        var task = await CreateTaskAsync(client, "Zamknij mnie");

        var closed = await PatchAsync(client, task.Id, new { isCompleted = true });
        Assert.Equal((TaskStatus.Completed, true), (closed.Status, closed.IsCompleted));

        var open = await client.GetFromJsonAsync<IntegrationTaskPageResponse>($"{Tasks}?isCompleted=false&limit=200", MindflowApiFactory.Json);
        Assert.DoesNotContain(open!.Items, item => item.Id == task.Id);

        var reopened = await PatchAsync(client, task.Id, new { isCompleted = false });
        Assert.Equal((TaskStatus.NotStarted, false), (reopened.Status, reopened.IsCompleted));
    }

    [LocalDatabaseFact]
    public async Task Patch_with_completed_status_marks_the_task_completed()
    {
        using var client = await CreateClientAsync();
        var task = await CreateTaskAsync(client, "Status wprost");

        var inProgress = await PatchAsync(client, task.Id, new { status = "InProgress" });
        Assert.Equal((TaskStatus.InProgress, false), (inProgress.Status, inProgress.IsCompleted));

        var completed = await PatchAsync(client, task.Id, new { status = "Completed" });
        Assert.Equal((TaskStatus.Completed, true), (completed.Status, completed.IsCompleted));
    }

    [LocalDatabaseFact]
    public async Task Is_completed_false_keeps_an_open_status()
    {
        using var client = await CreateClientAsync();
        var task = await CreateTaskAsync(client, "W trakcie");
        await PatchAsync(client, task.Id, new { status = "InProgress" });

        var unchanged = await PatchAsync(client, task.Id, new { isCompleted = false });

        Assert.Equal(TaskStatus.InProgress, unchanged.Status);
    }

    [LocalDatabaseFact]
    public async Task Contradicting_status_and_is_completed_is_rejected()
    {
        using var client = await CreateClientAsync();
        var task = await CreateTaskAsync(client, "Sprzeczność");

        var response = await client.PatchAsJsonAsync($"{Tasks}/{task.Id}", new { status = "Completed", isCompleted = false }, MindflowApiFactory.Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<HttpClient> CreateClientAsync()
    {
        var user = await factory.CreateUserAsync();
        var token = await factory.CreateManualTokenAsync(
            user.Id,
            IntegrationTokenScope.TasksRead,
            IntegrationTokenScope.TasksCreate,
            IntegrationTokenScope.TasksUpdate);
        return factory.CreateIntegrationClient(token);
    }

    private static async Task<TaskDetailResponse> CreateTaskAsync(HttpClient client, string content)
    {
        var response = await client.PostAsJsonAsync(Tasks, new { content }, MindflowApiFactory.Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TaskDetailResponse>(MindflowApiFactory.Json))!;
    }

    private static async Task<TaskDetailResponse> PatchAsync(HttpClient client, Guid taskId, object body)
    {
        var response = await client.PatchAsJsonAsync($"{Tasks}/{taskId}", body, MindflowApiFactory.Json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TaskDetailResponse>(MindflowApiFactory.Json))!;
    }
}
