using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Mindflow.Api.Repositories;
using Mindflow.Api.Services;

namespace Mindflow.Api.Hubs;

[Authorize]
public class TasksHub(
    ICurrentUserService currentUserService,
    ITaskRepository taskRepository) : Hub
{
    private const string UserGroupPrefix = "user:";
    private const string SpaceGroupPrefix = "space:";

    public override async Task OnConnectedAsync()
    {
        var principal = Context.User
            ?? throw new UnauthorizedAccessException("No user principal on hub connection.");

        var userId = await currentUserService.GetUserIdAsync(principal);

        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId));

        var spaceIds = await taskRepository.GetAccessibleSpaceIdsAsync(userId);
        foreach (var spaceId in spaceIds)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, SpaceGroup(spaceId));
        }

        await base.OnConnectedAsync();
    }

    public static string UserGroup(Guid userId) => $"{UserGroupPrefix}{userId}";
    public static string SpaceGroup(Guid spaceId) => $"{SpaceGroupPrefix}{spaceId}";
}
