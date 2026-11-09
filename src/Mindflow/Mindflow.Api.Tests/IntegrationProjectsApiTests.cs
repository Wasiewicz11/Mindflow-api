using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Tests.Infrastructure;

namespace Mindflow.Api.Tests;

public class IntegrationProjectsApiTests(MindflowApiFactory factory) : IClassFixture<MindflowApiFactory>
{
    private const string Projects = "/api/integration/v1/projects";

    [LocalDatabaseFact]
    public async Task Scoped_key_creates_projects_in_its_space()
    {
        using var world = await SpaceFixture.CreateAsync(factory);
        var scoped = await world.ScopedClientAsync();

        var created = await scoped.PostAsJsonAsync(Projects, new { name = "  Integracje  ", color = "#12ab34" }, MindflowApiFactory.Json);
        var outside = await scoped.PostAsJsonAsync(Projects, new { name = "Obcy", spaceId = world.OtherSpaceId }, MindflowApiFactory.Json);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var project = (await created.Content.ReadFromJsonAsync<IntegrationProjectResponse>(MindflowApiFactory.Json))!;
        Assert.Equal(("Integracje", "#12ab34", (Guid?)world.SpaceId), (project.Name, project.Color, project.SpaceId));
        Assert.Equal(HttpStatusCode.OK, (await scoped.GetAsync($"{Projects}/{project.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, outside.StatusCode);
        Assert.False(await factory.WithDbAsync(db => db.Projects.AnyAsync(p => p.Name == "Obcy" && p.UserId == world.UserId)));

        var recorded = await factory.WithDbAsync(db => db.TaskActivityEvents.SingleAsync(e =>
            e.ProjectId == project.Id && e.EventType == TaskActivityEventType.ProjectCreated));
        Assert.Equal((TaskActivityActorType.Integration, (Guid?)world.SpaceId), (recorded.ActorType, recorded.SpaceId));
        Assert.NotNull(recorded.IntegrationTokenId);
    }

    [LocalDatabaseFact]
    public async Task Unscoped_key_must_name_the_space()
    {
        using var world = await SpaceFixture.CreateAsync(factory);
        var unscoped = await world.UnscopedClientAsync();

        var withoutSpace = await unscoped.PostAsJsonAsync(Projects, new { name = "Gdzie?" }, MindflowApiFactory.Json);
        var withSpace = await unscoped.PostAsJsonAsync(Projects, new { name = "Dom 2", spaceId = world.OtherSpaceId }, MindflowApiFactory.Json);

        await AssertValidationProblemAsync(withoutSpace, "spaceId");
        Assert.Equal(HttpStatusCode.Created, withSpace.StatusCode);
    }

    [LocalDatabaseFact]
    public async Task Update_renames_and_recolors_only_projects_in_the_space()
    {
        using var world = await SpaceFixture.CreateAsync(factory);
        var scoped = await world.ScopedClientAsync();

        var own = await scoped.PatchAsJsonAsync($"{Projects}/{world.ProjectId}", new { name = "TMS 2", color = "#00ff00" }, MindflowApiFactory.Json);
        var other = await scoped.PatchAsJsonAsync($"{Projects}/{world.OtherProjectId}", new { name = "Przejęty" }, MindflowApiFactory.Json);
        var personal = await scoped.PatchAsJsonAsync($"{Projects}/{world.PersonalProjectId}", new { name = "Przejęty" }, MindflowApiFactory.Json);

        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        var updated = (await own.Content.ReadFromJsonAsync<IntegrationProjectResponse>(MindflowApiFactory.Json))!;
        Assert.Equal(("TMS 2", "#00ff00"), (updated.Name, updated.Color));
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, personal.StatusCode);
        var names = await factory.WithDbAsync(db => db.Projects
            .Where(project => project.UserId == world.UserId)
            .Select(project => project.Name)
            .ToListAsync());
        Assert.DoesNotContain("Przejęty", names);
        Assert.True(await factory.WithDbAsync(db => db.TaskActivityEvents.AnyAsync(e =>
            e.ProjectId == world.ProjectId && e.EventType == TaskActivityEventType.ProjectUpdated)));
    }

    [LocalDatabaseFact]
    public async Task Invalid_project_input_is_rejected()
    {
        using var world = await SpaceFixture.CreateAsync(factory);
        var scoped = await world.ScopedClientAsync();

        var badColor = await scoped.PostAsJsonAsync(Projects, new { name = "Kolor", color = "red" }, MindflowApiFactory.Json);
        var longName = await scoped.PostAsJsonAsync(Projects, new { name = new string('x', 41) }, MindflowApiFactory.Json);
        var blankName = await scoped.PatchAsJsonAsync($"{Projects}/{world.ProjectId}", new { name = "   " }, MindflowApiFactory.Json);

        await AssertValidationProblemAsync(badColor, "Color");
        await AssertValidationProblemAsync(longName, "Name");
        await AssertValidationProblemAsync(blankName, "name");
    }

    [LocalDatabaseFact]
    public async Task Project_writes_need_their_scopes_and_deletion_is_not_offered()
    {
        using var world = await SpaceFixture.CreateAsync(factory);
        var readOnly = await world.ScopedClientAsync(null, IntegrationTokenScope.ProjectsRead);
        var full = await world.ScopedClientAsync();

        var create = await readOnly.PostAsJsonAsync(Projects, new { name = "Nowy" }, MindflowApiFactory.Json);
        var update = await readOnly.PatchAsJsonAsync($"{Projects}/{world.ProjectId}", new { name = "Nowa nazwa" }, MindflowApiFactory.Json);
        var delete = await full.DeleteAsync($"{Projects}/{world.ProjectId}");

        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, delete.StatusCode);
        Assert.True(await factory.WithDbAsync(db => db.Projects.AnyAsync(project => project.Id == world.ProjectId)));
    }

    [LocalDatabaseFact]
    public async Task Project_changes_made_in_the_app_are_recorded()
    {
        using var world = await SpaceFixture.CreateAsync(factory);
        using var user = factory.CreateUserClient(world.UserId);

        var response = await user.PatchAsJsonAsync($"/spaces/{world.SpaceId}/projects/{world.ProjectId}", new { name = "TMS z aplikacji" }, MindflowApiFactory.Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var recorded = await factory.WithDbAsync(db => db.TaskActivityEvents.SingleAsync(e =>
            e.ProjectId == world.ProjectId && e.EventType == TaskActivityEventType.ProjectUpdated));
        Assert.Equal((TaskActivityActorType.User, (Guid?)null), (recorded.ActorType, recorded.IntegrationTokenId));
    }

    private static async Task AssertValidationProblemAsync(HttpResponseMessage response, string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.GetProperty("errors").TryGetProperty(field, out _), $"Expected an error for '{field}'.");
    }
}
