using Mindflow.Api.Models.Dtos;

namespace Mindflow.Api.Services.Integrations;

public interface IIntegrationTaskQueryService
{
    Task<IntegrationTaskPageResponse?> GetTasksAsync(IntegrationTaskQuery query, CancellationToken ct = default);

    Task<IntegrationTimeEntryPageResponse?> GetTimeEntriesAsync(
        Guid taskId,
        IntegrationPageQuery query,
        CancellationToken ct = default);

    /// <summary>False when the token is limited to a space and the entry lies outside it.</summary>
    Task<bool> CanReachTimeEntryAsync(Guid entryId, CancellationToken ct = default);
}
