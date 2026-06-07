using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Data;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Enums;

namespace Mindflow.Api.Repositories;

public class SuggestionRepository(MindflowDbContext db) : ISuggestionRepository
{
    public async Task<IReadOnlyList<AiSuggestion>> GetPendingForUserAsync(Guid userId)
        => await db.AiSuggestions
            .Include(s => s.Actions)
            .Where(s => s.UserId == userId && s.Status == SuggestionStatus.Pending)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();

    public async Task<AiSuggestion?> GetByIdWithActionsAsync(Guid id)
        => await db.AiSuggestions
            .Include(s => s.Actions)
            .FirstOrDefaultAsync(s => s.Id == id);

    public async Task AddAsync(AiSuggestion suggestion)
    {
        db.AiSuggestions.Add(suggestion);
        await db.SaveChangesAsync();
    }

    public async Task ExpirePendingForUserAsync(Guid userId)
        => await db.AiSuggestions
            .Where(s => s.UserId == userId && s.Status == SuggestionStatus.Pending)
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.Status, SuggestionStatus.Expired));

    public async Task<IReadOnlyDictionary<Guid, string>> GetTaskTitlesAsync(IReadOnlyCollection<Guid> taskIds)
        => await db.Tasks
            .Where(t => taskIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Content })
            .ToDictionaryAsync(x => x.Id, x => x.Content);

    public Task SaveChangesAsync() => db.SaveChangesAsync();
}
