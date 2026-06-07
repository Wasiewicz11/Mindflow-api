using Mindflow.Api.Models;

namespace Mindflow.Api.Repositories;

public interface ISuggestionRepository
{
    Task<IReadOnlyList<AiSuggestion>> GetPendingForUserAsync(Guid userId);
    Task<AiSuggestion?> GetByIdWithActionsAsync(Guid id);
    Task AddAsync(AiSuggestion suggestion);
    Task ExpirePendingForUserAsync(Guid userId);
    Task<IReadOnlyDictionary<Guid, string>> GetTaskTitlesAsync(IReadOnlyCollection<Guid> taskIds);
    Task SaveChangesAsync();
}
