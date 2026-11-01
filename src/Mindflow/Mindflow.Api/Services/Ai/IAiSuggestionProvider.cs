using Mindflow.Api.Models.Enums;

namespace Mindflow.Api.Services.Ai;

public interface IAiSuggestionProvider
{
    string Name { get; }

    /// <summary>The AI provider this implementation talks to; null for the local rule-based one.</summary>
    AiProvider? Provider { get; }

    Task<IReadOnlyList<SuggestionDraft>> GenerateAsync(DaySnapshot snapshot, AiCredentials? credentials, CancellationToken ct = default);
}
