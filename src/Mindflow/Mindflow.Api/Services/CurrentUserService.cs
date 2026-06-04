using System.Security.Claims;

namespace Mindflow.Api.Services;

public class CurrentUserService(IHttpContextAccessor httpContextAccessor) : ICurrentUserService
{
    public Task<Guid> GetCurrentUserIdAsync()
    {
        var principal = httpContextAccessor.HttpContext?.User
            ?? throw new UnauthorizedAccessException("No HTTP context.");

        var userIdString = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("Missing 'sub' claim.");

        if (!Guid.TryParse(userIdString, out var userId))
            throw new UnauthorizedAccessException("Invalid user ID in token.");

        return Task.FromResult(userId);
    }

    public Task<Guid> GetUserIdAsync(ClaimsPrincipal principal)
    {
        var userIdString = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("Missing 'sub' claim.");

        if (!Guid.TryParse(userIdString, out var userId))
            throw new UnauthorizedAccessException("Invalid user ID in token.");

        return Task.FromResult(userId);
    }
}
