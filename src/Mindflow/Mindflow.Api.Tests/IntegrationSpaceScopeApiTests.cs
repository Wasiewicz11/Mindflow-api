using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Tests.Infrastructure;

namespace Mindflow.Api.Tests;

public class IntegrationSpaceScopeApiTests(MindflowApiFactory factory) : IClassFixture<MindflowApiFactory>
{
    private const string Base = "/api/integration/v1";

    [LocalDatabaseFact]
    public async Task Projects_are_limited_to_the_key_space()
    {
        using var world = await SpaceFixture.CreateAsync(factory);
        var scoped = await world.ScopedClientAsync();
        var unscoped = await world.UnscopedClientAsync();

        var scopedProjects = await scoped.GetFromJsonAsync<IntegrationProjectPageResponse>($"{Base}/projects", MindflowApiFactory.Json);
        var allProjects = await unscoped.GetFromJsonAsync<IntegrationProjectPageResponse>($"{Base}/projects", MindflowApiFactory.Json);

        Assert.Equal([world.ProjectId], scopedProjects!.Items.Select(project => project.Id));
        Assert.Equal(1, scopedProjects.Total);
        Assert.Equal(3, allProjects!.Total);
    }

    [LocalDatabaseFact]
    public async Task Task_reads_are_limited_to_the_key_space()
    {
        using var world = await SpaceFixture.CreateAsync(factory);
        var scoped = await world.ScopedClientAsync();

        var page = await scoped.GetFromJsonAsync<IntegrationTaskPageResponse>($"{Base}/tasks", MindflowApiFactory.Json);

        Assert.Equal([world.TaskId], page!.Items.Select(task => task.Id));
        Assert.Equal(HttpStatusCode.OK, (await scoped.GetAsync($"{Base}/tasks/{world.TaskId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await scoped.GetAsync($"{Base}/tasks/{world.OtherTaskId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await scoped.GetAsync($"{Base}/tasks/{world.InboxTaskId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await scoped.GetAsync($"{Base}/tasks?projectId={world.OtherProjectId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await scoped.GetAsync($"{Base}/projects/{world.OtherProjectId}/tasks")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await scoped.GetAsync($"{Base}/tasks/{world.OtherTaskId}/subtasks")).StatusCode);
    }

    [LocalDatabaseFact]
    public async Task Task_writes_outside_the_key_space_return_not_found()
    {
        using var world = await SpaceFixture.CreateAsync(factory);
        var scoped = await world.ScopedClientAsync();

        var patchOther = await scoped.PatchAsJsonAsync($"{Base}/tasks/{world.OtherTaskId}", new { content = "Przejęte" }, MindflowApiFactory.Json);
        var moveOut = await scoped.PatchAsJsonAsync($"{Base}/tasks/{world.TaskId}", new { projectId = world.OtherProjectId }, MindflowApiFactory.Json);
        var createOutside = await scoped.PostAsJsonAsync($"{Base}/tasks", new { content = "Obce", projectId = world.OtherProjectId }, MindflowApiFactory.Json);
        var deleteOther = await scoped.DeleteAsync($"{Base}/tasks/{world.OtherTaskId}");
        var deleteInbox = await scoped.DeleteAsync($"{Base}/tasks/{world.InboxTaskId}");
        var subtaskOther = await scoped.PostAsJsonAsync($"{Base}/tasks/{world.OtherTaskId}/subtasks", new { content = "Krok" }, MindflowApiFactory.Json);

        Assert.All(
            [patchOther, moveOut, createOutside, deleteOther, deleteInbox, subtaskOther],
            response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));

        var remaining = await factory.WithDbAsync(db => db.Tasks
            .Where(task => task.UserId == world.UserId)
            .ToDictionaryAsync(task => task.Id, task => (task.Content, task.ProjectId)));
        Assert.Equal(("Zadanie domowe", world.OtherProjectId), remaining[world.OtherTaskId]);
        Assert.Equal(("Zadanie TMS", world.ProjectId), remaining[world.TaskId]);
        Assert.True(remaining.ContainsKey(world.InboxTaskId));
    }

    [LocalDatabaseFact]
    public async Task Task_without_project_needs_a_project_from_the_space()
    {
        using var world = await SpaceFixture.CreateAsync(factory);
        var scoped = await world.ScopedClientAsync();

        var withoutProject = await scoped.PostAsJsonAsync($"{Base}/tasks", new { content = "Do skrzynki?" }, MindflowApiFactory.Json);
        var upsertWithoutProject = await scoped.PutAsJsonAsync($"{Base}/tasks/external/jira/{Guid.NewGuid()}", new { content = "Z Jiry" }, MindflowApiFactory.Json);
        var inSpace = await SpaceFixture.CreateTaskAsync(scoped, "W projekcie", world.ProjectId);

        await AssertValidationProblemAsync(withoutProject, "projectId");
        await AssertValidationProblemAsync(upsertWithoutProject, "projectId");
        Assert.Equal(world.ProjectId, inSpace.ProjectId);
    }

    [LocalDatabaseFact]
    public async Task Task_without_project_lands_in_the_key_default_project()
    {
        using var world = await SpaceFixture.CreateAsync(factory);
        var scoped = await world.ScopedClientAsync(world.ProjectId);

        var created = await SpaceFixture.CreateTaskAsync(scoped, "Do domyślnego projektu", null);
        var upserted = await scoped.PutAsJsonAsync($"{Base}/tasks/external/jira/{Guid.NewGuid()}", new { content = "Z Jiry" }, MindflowApiFactory.Json);

        Assert.Equal(world.ProjectId, created.ProjectId);
        Assert.Equal(HttpStatusCode.Created, upserted.StatusCode);
        Assert.Equal(world.ProjectId, (await upserted.Content.ReadFromJsonAsync<TaskDetailResponse>(MindflowApiFactory.Json))!.ProjectId);
    }

    [LocalDatabaseFact]
    public async Task External_tasks_outside_the_key_space_are_invisible()
    {
        using var world = await SpaceFixture.CreateAsync(factory);
        var unscoped = await world.UnscopedClientAsync();
        var scoped = await world.ScopedClientAsync();
        var externalId = Guid.NewGuid().ToString();
        await unscoped.PutAsJsonAsync($"{Base}/tasks/external/jira/{externalId}", new { content = "Obce", projectId = world.OtherProjectId }, MindflowApiFactory.Json);

        var read = await scoped.GetAsync($"{Base}/tasks/external/jira/{externalId}");
        var update = await scoped.PutAsJsonAsync($"{Base}/tasks/external/jira/{externalId}", new { content = "Przejęte" }, MindflowApiFactory.Json);
        await scoped.DeleteAsync($"{Base}/tasks/external/jira/{externalId}");

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
        var stored = await factory.WithDbAsync(db => db.Tasks.SingleAsync(task => task.UserId == world.UserId && task.ExternalId == externalId));
        Assert.Equal("Obce", stored.Content);
    }

    [LocalDatabaseFact]
    public async Task Time_entries_are_limited_to_the_key_space()
    {
        using var world = await SpaceFixture.CreateAsync(factory);
        var unscoped = await world.UnscopedClientAsync();
        var scoped = await world.ScopedClientAsync();
        var otherEntryId = await LogTimeAsync(unscoped, world.OtherTaskId);

        var list = await scoped.GetAsync($"{Base}/tasks/{world.OtherTaskId}/time-entries");
        var create = await scoped.PostAsJsonAsync($"{Base}/tasks/{world.OtherTaskId}/time-entries", new { durationMinutes = 15 }, MindflowApiFactory.Json);
        var update = await scoped.PatchAsJsonAsync($"{Base}/time-entries/{otherEntryId}", new { durationMinutes = 90 }, MindflowApiFactory.Json);
        var delete = await scoped.DeleteAsync($"{Base}/time-entries/{otherEntryId}");

        Assert.All([list, create, update, delete], response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
        var stored = await factory.WithDbAsync(db => db.TaskTimeEntries.SingleAsync(entry => entry.Id == otherEntryId));
        Assert.Equal(30, stored.DurationMinutes);

        var ownEntryId = await LogTimeAsync(scoped, world.TaskId);
        var ownUpdate = await scoped.PatchAsJsonAsync($"{Base}/time-entries/{ownEntryId}", new { durationMinutes = 45 }, MindflowApiFactory.Json);
        Assert.Equal(HttpStatusCode.OK, ownUpdate.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await scoped.DeleteAsync($"{Base}/time-entries/{ownEntryId}")).StatusCode);
    }

    [LocalDatabaseFact]
    public async Task Key_settings_show_the_space_and_validate_it()
    {
        using var world = await SpaceFixture.CreateAsync(factory);
        var stranger = await factory.CreateUserAsync();
        var strangerSpace = await factory.CreateSpaceAsync(stranger.Id, "Cudza");
        using var user = factory.CreateUserClient(world.UserId);

        await world.ScopedClientAsync();
        var foreignSpace = await CreateTokenAsync(user, strangerSpace.Id, null);
        var projectOutsideSpace = await CreateTokenAsync(user, world.SpaceId, world.OtherProjectId);
        var projectWithoutSpace = await CreateTokenAsync(user, null, world.ProjectId);
        var settings = await user.GetFromJsonAsync<IntegrationSettingsResponse>("/integrations", MindflowApiFactory.Json);

        Assert.All(
            [foreignSpace, projectOutsideSpace, projectWithoutSpace],
            response => Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode));
        var token = Assert.Single(settings!.Tokens, token => token.SpaceId is not null);
        Assert.Equal((world.SpaceId, "KNS"), (token.SpaceId!.Value, token.SpaceName));
    }

    [LocalDatabaseFact]
    public async Task Key_stops_working_when_its_space_is_deleted()
    {
        using var world = await SpaceFixture.CreateAsync(factory);
        var scoped = await world.ScopedClientAsync();
        using var user = factory.CreateUserClient(world.UserId);

        Assert.Equal(HttpStatusCode.NoContent, (await user.DeleteAsync($"/spaces/{world.SpaceId}")).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await scoped.GetAsync($"{Base}/projects")).StatusCode);
    }

    private static async Task<Guid> LogTimeAsync(HttpClient client, Guid taskId)
    {
        var response = await client.PostAsJsonAsync($"{Base}/tasks/{taskId}/time-entries", new { durationMinutes = 30 }, MindflowApiFactory.Json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TaskTimeEntryMutationResponse>(MindflowApiFactory.Json))!.TimeEntry.Id;
    }

    private static Task<HttpResponseMessage> CreateTokenAsync(HttpClient user, Guid? spaceId, Guid? defaultProjectId) =>
        user.PostAsJsonAsync(
            "/integrations/tokens",
            new CreateIntegrationTokenRequest(
                "Klucz",
                [IntegrationTokenScope.TasksRead],
                DateTimeOffset.UtcNow.AddDays(30),
                spaceId,
                defaultProjectId),
            MindflowApiFactory.Json);

    private static async Task AssertValidationProblemAsync(HttpResponseMessage response, string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.GetProperty("errors").TryGetProperty(field, out _), $"Expected an error for '{field}'.");
    }
}
