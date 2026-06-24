using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Data;
using Mindflow.Api.Models;

namespace Mindflow.Api.Repositories;

public class PomodoroSessionRepository(MindflowDbContext db) : IPomodoroSessionRepository
{
    public Task<PomodoroSessionState?> GetByUserIdAsync(Guid userId) =>
        db.PomodoroSessions.FirstOrDefaultAsync(session => session.UserId == userId);

    public async Task<PomodoroSessionState> SaveAsync(PomodoroSessionState session)
    {
        if (db.Entry(session).State == EntityState.Detached)
            db.PomodoroSessions.Add(session);

        await db.SaveChangesAsync();
        return session;
    }

    public async Task DeleteAsync(PomodoroSessionState session)
    {
        db.PomodoroSessions.Remove(session);
        await db.SaveChangesAsync();
    }
}
