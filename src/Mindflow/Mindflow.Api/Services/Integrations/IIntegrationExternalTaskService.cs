using Mindflow.Api.Models.Dtos;

namespace Mindflow.Api.Services.Integrations;

public interface IIntegrationExternalTaskService
{
    Task<ExternalTaskUpsertResult> UpsertAsync(
        TaskExternalReference reference,
        UpsertExternalTaskRequest request,
        ExternalTaskPermissions permissions,
        CancellationToken ct = default);

    Task<TaskDetailResponse?> GetAsync(string source, string externalId, CancellationToken ct = default);

    Task DeleteAsync(string source, string externalId, CancellationToken ct = default);
}

public record ExternalTaskPermissions(bool CanCreate, bool CanUpdate);

public enum ExternalTaskUpsertOutcome
{
    Created,
    Updated,
    Forbidden,
    ContentRequired,
    ProjectNotFound
}

public record ExternalTaskUpsertResult(ExternalTaskUpsertOutcome Outcome, TaskDetailResponse? Task = null);
