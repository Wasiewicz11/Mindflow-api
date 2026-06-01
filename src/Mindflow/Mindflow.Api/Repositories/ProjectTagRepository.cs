using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Data;
using Mindflow.Api.Models;

namespace Mindflow.Api.Repositories;

public class ProjectTagRepository(MindflowDbContext db) : IProjectTagRepository
{
    public async Task<IReadOnlyList<string>> GetNamesForProjectAsync(Guid projectId)
    {
        return await db.ProjectTags
            .Where(t => t.ProjectId == projectId)
            .OrderBy(t => t.Name)
            .Select(t => t.Name)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<string>> EnsureExistAsync(Guid projectId, IReadOnlyCollection<string> names)
    {
        if (names.Count == 0) return Array.Empty<string>();

        var existing = await db.ProjectTags
            .Where(t => t.ProjectId == projectId)
            .Select(t => t.Name)
            .ToListAsync();

        var byLower = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in existing) byLower[name] = name;

        var canonical = new List<string>(names.Count);
        var toInsert = new List<ProjectTag>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in names)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var trimmed = raw.Trim();
            if (!seen.Add(trimmed)) continue;

            if (byLower.TryGetValue(trimmed, out var existingName))
            {
                canonical.Add(existingName);
            }
            else
            {
                canonical.Add(trimmed);
                toInsert.Add(new ProjectTag
                {
                    Id = Guid.NewGuid(),
                    ProjectId = projectId,
                    Name = trimmed,
                    CreatedAt = DateTimeOffset.UtcNow
                });
                byLower[trimmed] = trimmed;
            }
        }

        if (toInsert.Count > 0)
        {
            db.ProjectTags.AddRange(toInsert);
            await db.SaveChangesAsync();
        }

        return canonical;
    }
}
