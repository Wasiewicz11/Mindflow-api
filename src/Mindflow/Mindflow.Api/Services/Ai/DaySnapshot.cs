namespace Mindflow.Api.Services.Ai;

public record DaySnapshot(DateOnly Today, IReadOnlyList<SnapshotTask> Tasks);

public record SnapshotTask(
    int Ref,
    string Title,
    string Priority,
    string Status,
    bool HasDueDate,
    int? DaysUntilDue,
    int AgeDays,
    int TimesPostponed);
