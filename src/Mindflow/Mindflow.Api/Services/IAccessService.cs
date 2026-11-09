namespace Mindflow.Api.Services;

public interface IAccessService
{
    /// <summary>False for integration tokens limited to a space: their tasks must belong to a project.</summary>
    bool CanUseInbox { get; }
    Task<bool> CanAccessSpaceAsync(Guid spaceId, Guid userId);
    Task<bool> CanAccessProjectAsync(Guid projectId, Guid userId);
    Task<bool> CanAccessTaskAsync(Guid taskId, Guid userId);
}
