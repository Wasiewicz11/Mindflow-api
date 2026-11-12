using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Data;
using Mindflow.Api.Repositories;

namespace Mindflow.Api.Services.Ai.Dictation;

public class DictationContextBuilder(IProjectRepository projectRepository, MindflowDbContext db) : IDictationContextBuilder
{
    public async Task<DictationContext> BuildAsync(Guid userId, CancellationToken ct)
    {
        var timeZoneId = await db.Users.Where(u => u.Id == userId).Select(u => u.TimeZone).FirstOrDefaultAsync(ct);
        var zone = ResolveZone(timeZoneId);
        var local = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone);
        var projects = await projectRepository.GetAccessibleForUserAsync(userId, ct);

        return new DictationContext(
            DateOnly.FromDateTime(local.DateTime),
            TimeOnly.FromDateTime(local.DateTime),
            zone.Id,
            projects.Select(p => new DictationProject(p.Id, p.Name)).ToList());
    }

    private static TimeZoneInfo ResolveZone(string? id) =>
        !string.IsNullOrWhiteSpace(id) && TimeZoneInfo.TryFindSystemTimeZoneById(id.Trim(), out var zone) ? zone : TimeZoneInfo.Utc;
}
