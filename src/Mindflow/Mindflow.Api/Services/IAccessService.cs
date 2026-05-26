namespace Mindflow.Api.Services;

public interface IAccessService
{
    Task<bool> CanAccessProjectAsync(Guid projectId, Guid userId);
    Task<bool> CanAccessTaskAsync(Guid taskId, Guid userId);
}
