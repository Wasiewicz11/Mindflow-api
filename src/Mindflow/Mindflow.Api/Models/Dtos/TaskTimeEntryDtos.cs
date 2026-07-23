using System.ComponentModel.DataAnnotations;
using Mindflow.Api.Models.Enums;
using TaskStatus = Mindflow.Api.Models.Enums.TaskStatus;

namespace Mindflow.Api.Models.Dtos;

public record CreateTaskTimeEntryRequest(
    DateOnly? WorkDate,
    [Range(1, 1440)] int? DurationMinutes,
    DateTimeOffset? StartAt,
    DateTimeOffset? EndAt,
    [Range(0.01, 1000)] decimal? EstimatedHours,
    bool ClearEstimatedHours,
    [MaxLength(2000)] string? Notes);

public record UpdateTaskTimeEntryRequest(
    DateOnly? WorkDate,
    [Range(1, 1440)] int? DurationMinutes,
    DateTimeOffset? StartAt,
    DateTimeOffset? EndAt,
    [Range(0.01, 1000)] decimal? EstimatedHours,
    bool ClearEstimatedHours,
    [MaxLength(2000)] string? Notes);

public record CompleteTaskRequest(
    [Range(0.01, 1000)] decimal? EstimatedHours,
    bool ClearEstimatedHours,
    DateOnly? WorkDate,
    [Range(1, 1440)] int? DurationMinutes,
    DateTimeOffset? StartAt,
    DateTimeOffset? EndAt,
    [MaxLength(2000)] string? Notes);

public record TaskTimeEntryResponse(
    Guid Id,
    Guid UserId,
    Guid? TaskId,
    Guid? ProjectId,
    string TaskContent,
    TaskPriority TaskPriority,
    TaskStatus TaskStatus,
    IReadOnlyCollection<string> Tags,
    DateOnly WorkDate,
    int DurationMinutes,
    DateTimeOffset? StartAt,
    DateTimeOffset? EndAt,
    decimal? EstimatedHours,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record TaskTimeEntryMutationResponse(
    TaskTimeEntryResponse TimeEntry,
    TaskDetailResponse Task);

public record UpdateTaskTimeEntryResponse(
    TaskTimeEntryResponse TimeEntry,
    TaskDetailResponse? Task);

public record CompleteTaskResponse(
    TaskDetailResponse Task,
    TaskTimeEntryResponse? TimeEntry);
