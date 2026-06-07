namespace Mindflow.Api.Services.Ai;

public interface IAiSuggestionProvider
{
    string Name { get; }
    bool IsConfigured { get; }
    Task<IReadOnlyList<SuggestionDraft>> GenerateAsync(DaySnapshot snapshot, CancellationToken ct = default);
}
