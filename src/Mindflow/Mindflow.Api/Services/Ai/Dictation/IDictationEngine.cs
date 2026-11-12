using Mindflow.Api.Models.Enums;

namespace Mindflow.Api.Services.Ai.Dictation;

public record DictationEngineResult(string Json, string? Transcript, string Model);

public interface IDictationEngine
{
    AiProvider Provider { get; }
    Task<DictationEngineResult> RunAsync(AiCredentials credentials, DictationInput input, DictationContext context, IDictationPrompts prompts, CancellationToken ct);
}
