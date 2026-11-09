using System.Net;
using System.Net.Http.Json;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Tests.Infrastructure;
using TaskStatus = Mindflow.Api.Models.Enums.TaskStatus;

namespace Mindflow.Api.Tests;

public class IntegrationChangesApiTests(MindflowApiFactory factory) : IClassFixture<MindflowApiFactory>
{
    private const string Base = "/api/integration/v1";

    [LocalDatabaseFact]
    public async Task Feed_returns_changes_in_the_space_from_both_sides_in_order()
    {
        using var world = await SpaceFixture.CreateAsync(factory);
        var scoped = await world.ScopedClientAsync();
        var otherKey = await world.UnscopedClientAsync();
        using var user = factory.CreateUserClient(world.UserId);
        var start = await GetChangesAsync(scoped, "latest");

        var created = await SpaceFixture.CreateTaskAsync(scoped, "Od agenta", world.ProjectId);
        await otherKey.PatchAsJsonAsync($"{Base}/tasks/{world.TaskId}", new { priority = "P1" }, MindflowApiFactory.Json);
        await user.PutAsJsonAsync($"/tasks/{created.Id}", new { status = "Completed" }, MindflowApiFactory.Json);
        await user.PutAsJsonAsync($"/tasks/{world.OtherTaskId}", new { content = "Poza przestrzenią" }, MindflowApiFactory.Json);

        var page = await GetChangesAsync(scoped, start.NextCursor);

        Assert.Equal(
            [
                (TaskActivityEventType.TaskCreated, created.Id, "integration", true),
                (TaskActivityEventType.TaskPriorityChanged, world.TaskId, "integration", false),
                (TaskActivityEventType.TaskCompleted, created.Id, "user", false)
            ],
            page.Items.Select(item => (item.Type, item.TaskId!.Value, item.Actor.Type, item.Actor.IsCurrentKey)));
        var completed = page.Items[^1].Task!;
        Assert.Equal(("Od agenta", TaskStatus.Completed, true, (Guid?)world.ProjectId), (completed.Content, completed.Status, completed.IsCompleted, completed.ProjectId));
        Assert.Equal(TaskPriority.P1, page.Items[1].Task!.Priority);
        Assert.All(page.Items, item => Assert.Equal(world.SpaceId, item.SpaceId));
        Assert.False(page.HasMore);
        Assert.Equal(page.Items[^1].Cursor, page.NextCursor);

        var empty = await GetChangesAsync(scoped, page.NextCursor);
        Assert.Empty(empty.Items);
        Assert.Equal(page.NextCursor, empty.NextCursor);
    }

    [LocalDatabaseFact]
    public async Task Cursor_pages_through_changes_without_gaps()
    {
        using var world = await SpaceFixture.CreateAsync(factory);
        var scoped = await world.ScopedClientAsync();
        var start = await GetChangesAsync(scoped, "latest");
        for (var index = 0; index < 5; index++)
        {
            await SpaceFixture.CreateTaskAsync(scoped, $"Zadanie {index}", world.ProjectId);
        }

        var cursor = start.NextCursor;
        var seen = new List<IntegrationChangeResponse>();
        IntegrationChangesPageResponse page;
        do
        {
            page = await GetChangesAsync(scoped, cursor, 2);
            Assert.True(page.Items.Count <= 2);
            seen.AddRange(page.Items);
            cursor = page.NextCursor;
        } while (page.HasMore);

        Assert.Equal(5, seen.Count);
        Assert.Equal(5, seen.Select(item => item.Id).Distinct().Count());
        var sequence = seen.Select(item => long.Parse(item.Cursor)).ToList();
        Assert.Equal(sequence.Order(), sequence);
        Assert.Equal(["Zadanie 0", "Zadanie 1", "Zadanie 2", "Zadanie 3", "Zadanie 4"], seen.Select(item => item.Task!.Content));
    }

    [LocalDatabaseFact]
    public async Task Feed_reports_project_changes_and_deleted_tasks()
    {
        using var world = await SpaceFixture.CreateAsync(factory);
        var scoped = await world.ScopedClientAsync();
        using var user = factory.CreateUserClient(world.UserId);
        var start = await GetChangesAsync(scoped, "latest");

        var created = await scoped.PostAsJsonAsync($"{Base}/projects", new { name = "Nowy projekt" }, MindflowApiFactory.Json);
        var project = (await created.Content.ReadFromJsonAsync<IntegrationProjectResponse>(MindflowApiFactory.Json))!;
        await user.PatchAsJsonAsync($"/spaces/{world.SpaceId}/projects/{project.Id}", new { name = "Zmieniony w aplikacji" }, MindflowApiFactory.Json);
        await user.DeleteAsync($"/tasks/{world.TaskId}");

        var page = await GetChangesAsync(scoped, start.NextCursor);

        Assert.Equal(
            [TaskActivityEventType.ProjectCreated, TaskActivityEventType.ProjectUpdated, TaskActivityEventType.TaskDeleted],
            page.Items.Select(item => item.Type));
        Assert.Equal("Zmieniony w aplikacji", page.Items[0].Project!.Name);
        Assert.Equal(("user", project.Id), (page.Items[1].Actor.Type, page.Items[1].ProjectId!.Value));
        Assert.Equal(world.TaskId, page.Items[2].TaskId);
        Assert.Null(page.Items[2].Task);
    }

    [LocalDatabaseFact]
    public async Task Task_moved_out_of_the_space_shows_up_without_its_new_location()
    {
        using var world = await SpaceFixture.CreateAsync(factory);
        var scoped = await world.ScopedClientAsync();
        var unscoped = await world.UnscopedClientAsync();
        var start = await GetChangesAsync(scoped, "latest");

        await unscoped.PatchAsJsonAsync($"{Base}/tasks/{world.TaskId}", new { projectId = world.OtherProjectId }, MindflowApiFactory.Json);
        await unscoped.PatchAsJsonAsync($"{Base}/tasks/{world.TaskId}", new { content = "Już w innej przestrzeni" }, MindflowApiFactory.Json);

        var page = await GetChangesAsync(scoped, start.NextCursor);

        var moved = Assert.Single(page.Items);
        Assert.Equal((TaskActivityEventType.TaskProjectChanged, world.TaskId), (moved.Type, moved.TaskId!.Value));
        Assert.Null(moved.ProjectId);
        Assert.Null(moved.SpaceId);
        Assert.Null(moved.Task);
    }

    [LocalDatabaseFact]
    public async Task Unscoped_key_sees_every_space_and_its_own_inbox()
    {
        using var world = await SpaceFixture.CreateAsync(factory);
        var unscoped = await world.UnscopedClientAsync();
        var stranger = await factory.CreateUserAsync();
        using var strangerKey = factory.CreateIntegrationClient(await factory.CreateManualTokenAsync(stranger.Id, SpaceFixture.AllScopes));
        var start = await GetChangesAsync(unscoped, "latest");

        await unscoped.PatchAsJsonAsync($"{Base}/tasks/{world.OtherTaskId}", new { priority = "P2" }, MindflowApiFactory.Json);
        await unscoped.PatchAsJsonAsync($"{Base}/tasks/{world.InboxTaskId}", new { priority = "P2" }, MindflowApiFactory.Json);
        await SpaceFixture.CreateTaskAsync(strangerKey, "Cudze", null);

        var page = await GetChangesAsync(unscoped, start.NextCursor);

        Assert.Equal([world.OtherTaskId, world.InboxTaskId], page.Items.Select(item => item.TaskId!.Value));
        Assert.All(page.Items, item => Assert.NotNull(item.Task));
    }

    [LocalDatabaseFact]
    public async Task Logged_and_removed_time_shows_up_in_the_space()
    {
        using var world = await SpaceFixture.CreateAsync(factory);
        var scoped = await world.ScopedClientAsync();
        var start = await GetChangesAsync(scoped, "latest");

        var logged = await scoped.PostAsJsonAsync($"{Base}/tasks/{world.TaskId}/time-entries", new { durationMinutes = 20 }, MindflowApiFactory.Json);
        var entryId = (await logged.Content.ReadFromJsonAsync<TaskTimeEntryMutationResponse>(MindflowApiFactory.Json))!.TimeEntry.Id;
        await scoped.DeleteAsync($"{Base}/time-entries/{entryId}");

        var page = await GetChangesAsync(scoped, start.NextCursor);

        Assert.Equal(
            [TaskActivityEventType.TaskTimeSet, TaskActivityEventType.TaskTimeRemoved],
            page.Items.Select(item => item.Type));
    }

    [LocalDatabaseFact]
    public async Task Feed_validates_the_cursor_and_requires_tasks_read()
    {
        using var world = await SpaceFixture.CreateAsync(factory);
        var scoped = await world.ScopedClientAsync();
        var withoutRead = await world.ScopedClientAsync(null, IntegrationTokenScope.ProjectsRead);

        Assert.Equal(HttpStatusCode.BadRequest, (await scoped.GetAsync($"{Base}/changes?since=abc")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await scoped.GetAsync($"{Base}/changes?since=-1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await withoutRead.GetAsync($"{Base}/changes")).StatusCode);
    }

    private static async Task<IntegrationChangesPageResponse> GetChangesAsync(HttpClient client, string since, int? limit = null)
    {
        var url = $"{Base}/changes?since={since}" + (limit is null ? "" : $"&limit={limit}");
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IntegrationChangesPageResponse>(MindflowApiFactory.Json))!;
    }
}
