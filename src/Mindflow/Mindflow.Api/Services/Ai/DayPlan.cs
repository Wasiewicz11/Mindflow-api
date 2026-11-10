namespace Mindflow.Api.Services.Ai;

public record DayPlan(
    string Summary,
    IReadOnlyList<DayPlanItem> Timeline,
    IReadOnlyList<string> Focus,
    IReadOnlyList<string> Risks,
    string? Tip,
    bool FromAi);

public record DayPlanItem(TimeOnly Start, TimeOnly End, string Title, string? Note);

public record DayPlanContext(
    DateOnly Date,
    TimeOnly DayStart,
    IReadOnlyList<DayPlanBlock> Blocks,
    IReadOnlyList<DayPlanTask> Tasks);

public record DayPlanBlock(TimeOnly Start, TimeOnly End, string Title, int? TaskRef);

public record DayPlanTask(
    int Ref,
    string Title,
    string Priority,
    string Status,
    int? DaysUntilDue,
    TimeOnly? DueTime,
    decimal? EstimatedHours,
    int TimesPostponed);
