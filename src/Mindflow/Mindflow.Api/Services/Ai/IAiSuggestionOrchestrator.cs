namespace Mindflow.Api.Services.Ai;

public interface IAiSuggestionOrchestrator
{
    Task<OrchestratorResult> GenerateAsync(DaySnapshot snapshot, bool aiAllowed, CancellationToken ct = default);
}

public record OrchestratorResult(string? ProviderName, bool UsedAi, IReadOnlyList<SuggestionDraft> Drafts);
