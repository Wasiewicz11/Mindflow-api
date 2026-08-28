using Mindflow.Api.Models.Dtos;

namespace Mindflow.Api.Services.Integrations;

public interface IIntegrationTaskQueryService
{
    Task<IntegrationTaskPageResponse?> GetTasksAsync(IntegrationTaskQuery query, CancellationToken ct = default);

    Task<IntegrationTimeEntryPageResponse?> GetTimeEntriesAsync(
        Guid taskId,
        IntegrationPageQuery query,
        CancellationToken ct = default);
}
