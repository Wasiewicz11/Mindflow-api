using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Data;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Repositories;

namespace Mindflow.Api.Services.Notes;

public class NoteSuggestionReader(MindflowDbContext db, IProjectRepository projects)
{
    public async Task<IReadOnlyList<NoteSuggestionResponse>> ReadAsync(Guid userId, Guid noteId, CancellationToken ct)
    {
        var suggestions = await db.NoteTaskSuggestions.AsNoTracking()
            .Where(s => s.NoteId == noteId)
            .OrderBy(s => s.Order)
            .ToListAsync(ct);
        if (suggestions.Count == 0) return [];

        var names = await ProjectNamesAsync(userId, suggestions.Any(s => s.ProjectId is not null), ct);
        return suggestions.Select(s => NoteMapper.ToSuggestion(s, s.ProjectId is { } p ? names.GetValueOrDefault(p) : null)).ToList();
    }

    public async Task<Dictionary<Guid, string>> ProjectNamesAsync(Guid userId, bool needed, CancellationToken ct) =>
        needed
            ? (await projects.GetAccessibleForUserAsync(userId, ct)).ToDictionary(p => p.Id, p => p.Name)
            : [];
}
