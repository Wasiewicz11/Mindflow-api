using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Data;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Enums;

namespace Mindflow.Api.Services;

public class AuthService(MindflowDbContext db) : IAuthService
{
    public async Task RegisterAsync(string sub, string email, AuthProvider provider)
    {
        var exists = await db.UserIdentities.AnyAsync(ui =>
            ui.Provider == provider && ui.ProviderUserId == sub);

        if (exists) return;

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            TimeZone = "UTC",
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.Users.Add(user);
        db.UserIdentities.Add(new UserIdentity
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Provider = provider,
            ProviderUserId = sub,
            CreatedAt = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync();
    }
}
