using System.Net;
using System.Net.Http.Json;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Tests.Infrastructure;

namespace Mindflow.Api.Tests;

public class IntegrationSpacesApiTests(MindflowApiFactory factory) : IClassFixture<MindflowApiFactory>
{
    private const string Spaces = "/api/integration/v1/spaces";

    [LocalDatabaseFact]
    public async Task Scoped_key_sees_only_its_space()
    {
        using var world = await SpaceFixture.CreateAsync(factory);
        var scoped = await world.ScopedClientAsync();

        var spaces = await scoped.GetFromJsonAsync<List<IntegrationSpaceResponse>>(Spaces, MindflowApiFactory.Json);
        var own = await scoped.GetFromJsonAsync<IntegrationSpaceResponse>($"{Spaces}/{world.SpaceId}", MindflowApiFactory.Json);
        var other = await scoped.GetAsync($"{Spaces}/{world.OtherSpaceId}");

        var space = Assert.Single(spaces!);
        Assert.Equal((world.SpaceId, "KNS", SpaceRole.Owner), (space.Id, space.Name, space.Role));
        Assert.Equal(world.SpaceId, own!.Id);
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
    }

    [LocalDatabaseFact]
    public async Task Unscoped_key_sees_owned_and_shared_spaces_only()
    {
        using var world = await SpaceFixture.CreateAsync(factory);
        var owner = await factory.CreateUserAsync();
        var shared = await factory.CreateSpaceAsync(owner.Id, "Wspólna");
        var foreign = await factory.CreateSpaceAsync(owner.Id, "Cudza");
        await factory.WithDbAsync(async db =>
        {
            db.SpaceMembers.Add(new SpaceMember
            {
                Id = Guid.NewGuid(),
                SpaceId = shared.Id,
                UserId = world.UserId,
                Role = SpaceRole.Member,
                JoinedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        });
        var unscoped = await world.UnscopedClientAsync();

        var spaces = await unscoped.GetFromJsonAsync<List<IntegrationSpaceResponse>>(Spaces, MindflowApiFactory.Json);

        Assert.Equal(
            [(world.SpaceId, SpaceRole.Owner), (world.OtherSpaceId, SpaceRole.Owner), (shared.Id, SpaceRole.Member)],
            spaces!.Select(space => (space.Id, space.Role)));
        Assert.Equal(HttpStatusCode.NotFound, (await unscoped.GetAsync($"{Spaces}/{foreign.Id}")).StatusCode);
        await factory.WithDbAsync(async db =>
        {
            db.SpaceMembers.RemoveRange(db.SpaceMembers.Where(member => member.SpaceId == shared.Id));
            await db.SaveChangesAsync();
        });
    }

    [LocalDatabaseFact]
    public async Task Spaces_require_the_spaces_read_scope()
    {
        using var world = await SpaceFixture.CreateAsync(factory);
        var scoped = await world.ScopedClientAsync(null, IntegrationTokenScope.TasksRead);

        Assert.Equal(HttpStatusCode.Forbidden, (await scoped.GetAsync(Spaces)).StatusCode);
    }
}
