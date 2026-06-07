namespace Mindflow.Api.Services.Ai;

public interface IAiSuggestionProvider
{
    string Name { get; }
    bool IsConfigured { get; }
    bool IsAi { get; }
    Task<IReadOnlyList<SuggestionDraft>> GenerateAsync(DaySnapshot snapshot, CancellationToken ct = default);
}
