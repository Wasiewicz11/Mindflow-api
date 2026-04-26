using System.Security.Claims;

namespace Mindflow.Api.Services;

public interface ICurrentUserService
{
    Task<Guid> GetCurrentUserIdAsync();
    Task<Guid> GetUserIdAsync(ClaimsPrincipal principal);
}
