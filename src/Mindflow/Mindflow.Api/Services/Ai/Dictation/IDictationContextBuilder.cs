namespace Mindflow.Api.Services.Ai.Dictation;

public interface IDictationContextBuilder
{
    Task<DictationContext> BuildAsync(Guid userId, CancellationToken ct);
}
