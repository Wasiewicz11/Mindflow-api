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

    public async Task<IReadOnlyList<string>> RenameAsync(Guid projectId, string currentName, string newName)
    {
        var tag = await db.ProjectTags
            .FirstOrDefaultAsync(t => t.ProjectId == projectId && t.Name.ToLower() == currentName.ToLower());
        if (tag is null) return await GetNamesForProjectAsync(projectId);

        var oldName = tag.Name;
        var existingTarget = await db.ProjectTags
            .FirstOrDefaultAsync(t => t.ProjectId == projectId
                                      && t.Id != tag.Id
                                      && t.Name.ToLower() == newName.ToLower());

        var canonicalNewName = existingTarget?.Name ?? newName;
        if (existingTarget is not null)
        {
            db.ProjectTags.Remove(tag);
        }
        else
        {
            tag.Name = newName;
        }

        var tasks = await db.Tasks
            .Where(t => t.ProjectId == projectId)
            .ToListAsync();

        foreach (var task in tasks)
        {
            task.Tags = ReplaceTag(task.Tags, oldName, canonicalNewName);
        }

        await db.SaveChangesAsync();
        return await GetNamesForProjectAsync(projectId);
    }

    public async Task<IReadOnlyList<string>> DeleteAsync(Guid projectId, string name)
    {
        var tags = await db.ProjectTags
            .Where(t => t.ProjectId == projectId && t.Name.ToLower() == name.ToLower())
            .ToListAsync();

        if (tags.Count > 0)
        {
            db.ProjectTags.RemoveRange(tags);
        }

        var tasks = await db.Tasks
            .Where(t => t.ProjectId == projectId)
            .ToListAsync();

        foreach (var task in tasks)
        {
            task.Tags = task.Tags
                .Where(tag => !string.Equals(tag, name, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        await db.SaveChangesAsync();
        return await GetNamesForProjectAsync(projectId);
    }

    private static List<string> ReplaceTag(IReadOnlyCollection<string> tags, string currentName, string newName)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tag in tags)
        {
            var next = string.Equals(tag, currentName, StringComparison.OrdinalIgnoreCase)
                ? newName
                : tag;

            if (seen.Add(next))
            {
                result.Add(next);
            }
        }

        return result;
    }
}
