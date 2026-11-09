using System.Net;
using System.Net.Http.Json;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Models.Enums;

namespace Mindflow.Api.Tests.Infrastructure;

/// <summary>One user with two spaces, a project in each, a personal project and a task in every place.</summary>
public sealed record SpaceFixture(
    MindflowApiFactory Factory,
    Guid UserId,
    Guid SpaceId,
    Guid OtherSpaceId,
    Guid ProjectId,
    Guid OtherProjectId,
    Guid PersonalProjectId,
    Guid TaskId,
    Guid OtherTaskId,
    Guid InboxTaskId) : IDisposable
{
    public static readonly IntegrationTokenScope[] AllScopes = Enum.GetValues<IntegrationTokenScope>();

    private readonly List<HttpClient> clients = [];

    public static async Task<SpaceFixture> CreateAsync(MindflowApiFactory factory)
    {
        var user = await factory.CreateUserAsync();
        var space = await factory.CreateSpaceAsync(user.Id, "KNS");
        var otherSpace = await factory.CreateSpaceAsync(user.Id, "Prywatne");
        var project = await factory.CreateProjectAsync(user.Id, "TMS", space.Id);
        var otherProject = await factory.CreateProjectAsync(user.Id, "Dom", otherSpace.Id);
        var personalProject = await factory.CreateProjectAsync(user.Id, "Bez przestrzeni");

        using var admin = factory.CreateIntegrationClient(await factory.CreateManualTokenAsync(user.Id, AllScopes));
        var task = await CreateTaskAsync(admin, "Zadanie TMS", project.Id);
        var otherTask = await CreateTaskAsync(admin, "Zadanie domowe", otherProject.Id);
        var inboxTask = await CreateTaskAsync(admin, "Zadanie w skrzynce", null);

        return new SpaceFixture(
            factory,
            user.Id,
            space.Id,
            otherSpace.Id,
            project.Id,
            otherProject.Id,
            personalProject.Id,
            task.Id,
            otherTask.Id,
            inboxTask.Id);
    }

    public async Task<HttpClient> UnscopedClientAsync(params IntegrationTokenScope[] scopes)
    {
        var client = Factory.CreateIntegrationClient(
            await Factory.CreateManualTokenAsync(UserId, scopes.Length == 0 ? AllScopes : scopes));
        clients.Add(client);
        return client;
    }

    public async Task<HttpClient> ScopedClientAsync(Guid? defaultProjectId = null, params IntegrationTokenScope[] scopes)
    {
        var client = Factory.CreateIntegrationClient(
            await Factory.CreateSpaceTokenAsync(UserId, SpaceId, defaultProjectId, scopes.Length == 0 ? AllScopes : scopes));
        clients.Add(client);
        return client;
    }

    public static async Task<TaskDetailResponse> CreateTaskAsync(HttpClient client, string content, Guid? projectId)
    {
        var response = await client.PostAsJsonAsync("/api/integration/v1/tasks", new { content, projectId }, MindflowApiFactory.Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TaskDetailResponse>(MindflowApiFactory.Json))!;
    }

    public void Dispose()
    {
        foreach (var client in clients) client.Dispose();
    }
}
