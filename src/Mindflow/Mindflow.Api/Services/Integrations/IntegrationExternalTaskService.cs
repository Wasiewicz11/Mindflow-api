using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Data;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Repositories;
using Npgsql;

namespace Mindflow.Api.Services.Integrations;

public class IntegrationExternalTaskService(
    MindflowDbContext db,
    ICurrentUserService currentUserService,
    ITaskRepository taskRepository,
    ITaskService taskService) : IIntegrationExternalTaskService
{
    private const string ExternalReferenceIndex = "ix_tasks_user_id_external_source_external_id";

    public async Task<ExternalTaskUpsertResult> UpsertAsync(
        TaskExternalReference reference,
        UpsertExternalTaskRequest request,
        ExternalTaskPermissions permissions,
        CancellationToken ct = default)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        var existingId = await taskRepository.FindIdByExternalReferenceAsync(userId, reference.Source, reference.ExternalId, ct);

        if (existingId is null)
        {
            if (!permissions.CanCreate) return new ExternalTaskUpsertResult(ExternalTaskUpsertOutcome.Forbidden);
            if (string.IsNullOrWhiteSpace(request.Content)) return new ExternalTaskUpsertResult(ExternalTaskUpsertOutcome.ContentRequired);

            try
            {
                var created = await taskService.CreateAsync(ToCreateRequest(request), reference);
                return created is null
                    ? new ExternalTaskUpsertResult(ExternalTaskUpsertOutcome.ProjectNotFound)
                    : new ExternalTaskUpsertResult(ExternalTaskUpsertOutcome.Created, created);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: ExternalReferenceIndex
            })
            {
                db.ChangeTracker.Clear();
                existingId = await taskRepository.FindIdByExternalReferenceAsync(userId, reference.Source, reference.ExternalId, ct)
                    ?? throw new InvalidOperationException("External task disappeared after a concurrent create.");
            }
        }

        if (!permissions.CanUpdate) return new ExternalTaskUpsertResult(ExternalTaskUpsertOutcome.Forbidden);

        var updated = await taskService.UpdateAsync(existingId.Value, ToUpdateRequest(request), reference);
        return updated is null
            ? new ExternalTaskUpsertResult(ExternalTaskUpsertOutcome.ProjectNotFound)
            : new ExternalTaskUpsertResult(ExternalTaskUpsertOutcome.Updated, updated);
    }

    public async Task<TaskDetailResponse?> GetAsync(string source, string externalId, CancellationToken ct = default)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        var id = await taskRepository.FindIdByExternalReferenceAsync(userId, source, externalId, ct);

        return id is null ? null : await taskService.GetByIdAsync(id.Value);
    }

    public async Task DeleteAsync(string source, string externalId, CancellationToken ct = default)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        var id = await taskRepository.FindIdByExternalReferenceAsync(userId, source, externalId, ct);

        if (id is not null) await taskService.DeleteAsync(id.Value);
    }

    private static CreateTaskRequest ToCreateRequest(UpsertExternalTaskRequest request) =>
        new(
            request.Content!,
            request.Description,
            request.Priority,
            request.Status,
            request.DueDate,
            request.DueTime,
            request.EstimatedHours,
            request.ProjectId,
            request.Tags,
            null);

    private static UpdateTaskRequest ToUpdateRequest(UpsertExternalTaskRequest request) =>
        new(
            request.Content,
            request.Description,
            request.Priority,
            request.Status,
            request.DueDate,
            false,
            request.DueTime,
            false,
            request.EstimatedHours,
            false,
            request.ProjectId,
            request.Tags);
}
