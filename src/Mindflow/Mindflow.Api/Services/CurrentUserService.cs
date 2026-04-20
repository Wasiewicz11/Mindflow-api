using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Data;

namespace Mindflow.Api.Services;

public class CurrentUserService(
    IHttpContextAccessor httpContextAccessor, 
    MindflowDbContext db
    ) : ICurrentUserService
{
    public async Task<Guid> GetCurrentUserIdAsync()
    {
        var claims = httpContextAccessor.HttpContext?.User
            ?? throw new UnauthorizedAccessException("No HTTP context.");

        var sub = claims.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? claims.FindFirstValue("sub")
            ?? throw new UnauthorizedAccessException("Missing 'sub' claim.");

        var provider = AuthProviderResolver.Resolve(claims);

        var identity = await db.UserIdentities
            .FirstOrDefaultAsync(ui => ui.Provider == provider && ui.ProviderUserId == sub)
            ?? throw new UnauthorizedAccessException("User not registered.");

        return identity.UserId;
    }
}
