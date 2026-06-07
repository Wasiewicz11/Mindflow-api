namespace Mindflow.Api.Services.Ai;

public interface IAiSuggestionOrchestrator
{
    Task<OrchestratorResult> GenerateAsync(DaySnapshot snapshot, CancellationToken ct = default);
}

public record OrchestratorResult(string? ProviderName, IReadOnlyList<SuggestionDraft> Drafts);
