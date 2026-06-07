using Mindflow.Api.Models.Dtos;

namespace Mindflow.Api.Services;

public interface ISuggestionService
{
    Task<int> GenerateForUserAsync(Guid userId, CancellationToken ct = default);
    Task<GenerateSuggestionsResponse> GenerateOnDemandAsync();
    Task<SuggestionQuotaResponse> GetQuotaAsync();
    Task<IReadOnlyList<SuggestionResponse>> GetPendingAsync();
    Task<bool> AcceptAsync(Guid suggestionId);
    Task<bool> RejectAsync(Guid suggestionId);
}
