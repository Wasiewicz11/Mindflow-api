using Mindflow.Api.Models.Dtos;

namespace Mindflow.Api.Services.Ai.Dictation;

public record DictationReservation(AiCredentials Credentials, bool Lite, AiUsageResponse Usage);

public interface IDictationQuota
{
    Task<DictationReservation> ReserveAsync(Guid userId, AiCredentials credentials, CancellationToken ct);
}
