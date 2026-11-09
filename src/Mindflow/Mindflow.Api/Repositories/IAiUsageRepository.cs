namespace Mindflow.Api.Repositories;

public interface IAiUsageRepository
{
    Task<int> GetAiCallsAsync(Guid userId, DateOnly date);
    Task IncrementAiCallsAsync(Guid userId, DateOnly date);
    Task<int> GetDictationCallsAsync(Guid userId, DateOnly date, CancellationToken ct = default);
    Task IncrementDictationCallsAsync(Guid userId, DateOnly date, CancellationToken ct = default);
}
