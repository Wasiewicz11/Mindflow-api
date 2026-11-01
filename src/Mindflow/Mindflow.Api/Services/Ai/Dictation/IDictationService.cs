using Mindflow.Api.Models.Dtos;

namespace Mindflow.Api.Services.Ai.Dictation;

public interface IDictationService
{
    Task<DictationResponse> ProcessAsync(DictationInput input, CancellationToken ct = default);
}
