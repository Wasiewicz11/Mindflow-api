using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Tests.Infrastructure;
using TaskStatus = Mindflow.Api.Models.Enums.TaskStatus;

namespace Mindflow.Api.Tests;

public class ExternalTaskApiTests(MindflowApiFactory factory) : IClassFixture<MindflowApiFactory>
{
    private const string Base = "/api/integration/v1/tasks/external";

    private static readonly IntegrationTokenScope[] AllTaskScopes =
    [
        IntegrationTokenScope.TasksRead,
        IntegrationTokenScope.TasksCreate,
        IntegrationTokenScope.TasksUpdate,
        IntegrationTokenScope.TasksDelete
    ];

    [LocalDatabaseFact]
    public async Task Upsert_creates_then_updates_the_same_task()
    {
        var (client, userId) = await CreateClientAsync(AllTaskScopes);
        var externalId = Guid.NewGuid().ToString();

        var created = await client.PutAsJsonAsync($"{Base}/jira/{externalId}", Body("Zlecenie · Montaż", "https://jira.example.com/1"), MindflowApiFactory.Json);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var createdTask = await ReadTaskAsync(created);
        Assert.Equal(("jira", externalId, "https://jira.example.com/1"), (createdTask.ExternalSource, createdTask.ExternalId, createdTask.ExternalUrl));

        var updated = await client.PutAsJsonAsync($"{Base}/jira/{externalId}", Body("Zlecenie · Montaż v2", null, TaskStatus.Completed), MindflowApiFactory.Json);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var updatedTask = await ReadTaskAsync(updated);
        Assert.Equal(createdTask.Id, updatedTask.Id);
        Assert.Equal("Zlecenie · Montaż v2", updatedTask.Content);
        Assert.Equal(TaskStatus.Completed, updatedTask.Status);
        Assert.True(updatedTask.IsCompleted);
        Assert.Equal("https://jira.example.com/1", updatedTask.ExternalUrl);
        Assert.Equal(1, await CountExternalTasksAsync(userId, "jira", externalId));
    }

    [LocalDatabaseFact]
    public async Task Repeated_identical_upsert_keeps_a_single_task()
    {
        var (client, userId) = await CreateClientAsync(AllTaskScopes);
        var externalId = Guid.NewGuid().ToString();
        var body = Body("Ta sama treść", null);

        var first = await client.PutAsJsonAsync($"{Base}/jira/{externalId}", body, MindflowApiFactory.Json);
        var second = await client.PutAsJsonAsync($"{Base}/jira/{externalId}", body, MindflowApiFactory.Json);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal((await ReadTaskAsync(first)).Id, (await ReadTaskAsync(second)).Id);
        Assert.Equal(1, await CountExternalTasksAsync(userId, "jira", externalId));
    }

    [LocalDatabaseFact]
    public async Task Get_and_delete_by_external_id()
    {
        var (client, _) = await CreateClientAsync(AllTaskScopes);
        var externalId = Guid.NewGuid().ToString();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{Base}/jira/{externalId}")).StatusCode);

        await client.PutAsJsonAsync($"{Base}/jira/{externalId}", Body("Do usunięcia", null), MindflowApiFactory.Json);
        var fetched = await client.GetAsync($"{Base}/jira/{externalId}");
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        Assert.Equal(externalId, (await ReadTaskAsync(fetched)).ExternalId);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{Base}/jira/{externalId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{Base}/jira/{externalId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{Base}/jira/{externalId}")).StatusCode);
    }

    [LocalDatabaseFact]
    public async Task Create_requires_tasks_create_scope()
    {
        var (client, _) = await CreateClientAsync(IntegrationTokenScope.TasksUpdate);

        var response = await client.PutAsJsonAsync($"{Base}/jira/{Guid.NewGuid()}", Body("Bez uprawnień", null), MindflowApiFactory.Json);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [LocalDatabaseFact]
    public async Task Update_requires_tasks_update_scope()
    {
        var user = await factory.CreateUserAsync();
        var creator = factory.CreateIntegrationClient(await factory.CreateManualTokenAsync(user.Id, IntegrationTokenScope.TasksCreate));
        var externalId = Guid.NewGuid().ToString();

        var first = await creator.PutAsJsonAsync($"{Base}/jira/{externalId}", Body("Pierwsza wersja", null), MindflowApiFactory.Json);
        var second = await creator.PutAsJsonAsync($"{Base}/jira/{externalId}", Body("Druga wersja", null), MindflowApiFactory.Json);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, second.StatusCode);
    }

    [LocalDatabaseFact]
    public async Task Invalid_reference_returns_problem_details()
    {
        var (client, _) = await CreateClientAsync(AllTaskScopes);

        var badSource = await client.PutAsJsonAsync($"{Base}/Jira_Cloud/1", Body("X", null), MindflowApiFactory.Json);
        var badUrl = await client.PutAsJsonAsync($"{Base}/jira/1", Body("X", "http://jira.example.com/1"), MindflowApiFactory.Json);
        var longId = await client.PutAsJsonAsync($"{Base}/jira/{new string('x', 201)}", Body("X", null), MindflowApiFactory.Json);

        await AssertValidationProblemAsync(badSource, "source");
        await AssertValidationProblemAsync(badUrl, "externalUrl");
        await AssertValidationProblemAsync(longId, "externalId");
    }

    [LocalDatabaseFact]
    public async Task Create_without_content_is_rejected()
    {
        var (client, _) = await CreateClientAsync(AllTaskScopes);

        var response = await client.PutAsJsonAsync($"{Base}/jira/{Guid.NewGuid()}", Body(null, null), MindflowApiFactory.Json);

        await AssertValidationProblemAsync(response, "content");
    }

    private async Task<(HttpClient Client, Guid UserId)> CreateClientAsync(params IntegrationTokenScope[] scopes)
    {
        var user = await factory.CreateUserAsync();
        var token = await factory.CreateManualTokenAsync(user.Id, scopes);
        return (factory.CreateIntegrationClient(token), user.Id);
    }

    private static UpsertExternalTaskRequest Body(string? content, string? url, TaskStatus? status = null) =>
        new(content, null, null, status, null, null, null, null, null, url);

    private static async Task<TaskDetailResponse> ReadTaskAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<TaskDetailResponse>(MindflowApiFactory.Json))!;

    private Task<int> CountExternalTasksAsync(Guid userId, string source, string externalId) =>
        factory.WithDbAsync(db => db.Tasks.CountAsync(task =>
            task.UserId == userId && task.ExternalSource == source && task.ExternalId == externalId));

    private static async Task AssertValidationProblemAsync(HttpResponseMessage response, string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.GetProperty("errors").TryGetProperty(field, out _), $"Expected an error for '{field}'.");
    }
}
