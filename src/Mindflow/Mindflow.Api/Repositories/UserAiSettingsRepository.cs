using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Data;
using Mindflow.Api.Models;

namespace Mindflow.Api.Repositories;

public class UserAiSettingsRepository(MindflowDbContext db) : IUserAiSettingsRepository
{
    public Task<UserAiSettings?> GetByUserIdAsync(Guid userId, CancellationToken ct = default) =>
        db.UserAiSettings.FirstOrDefaultAsync(s => s.UserId == userId, ct);

    public async Task SaveAsync(UserAiSettings settings, CancellationToken ct = default)
    {
        if (db.Entry(settings).State == EntityState.Detached) db.UserAiSettings.Add(settings);
        await db.SaveChangesAsync(ct);
    }
}
