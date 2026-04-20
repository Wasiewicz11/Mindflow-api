namespace Mindflow.Api.Services;

public interface ICurrentUserService
{
    Task<Guid> GetCurrentUserIdAsync();
}
