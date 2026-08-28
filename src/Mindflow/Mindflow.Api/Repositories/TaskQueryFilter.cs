using TaskStatus = Mindflow.Api.Models.Enums.TaskStatus;

namespace Mindflow.Api.Repositories;

public record TaskQueryFilter(
    Guid? ProjectId,
    TaskStatus? Status,
    bool? IsCompleted,
    DateOnly? DueBefore,
    DateTimeOffset? CreatedAfter,
    int Limit,
    int Offset);
