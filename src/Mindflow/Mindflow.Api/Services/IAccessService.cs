namespace Mindflow.Api.Services;

public interface IAccessService
{
    Task<bool> CanAccessProjectAsync(Guid projectId, Guid userId);
}
