namespace Mindflow.Api.Services.Ai;

public interface IDayPlanWriter
{
    /// <summary>Plan of the user's local day; falls back to a rule-based plan when AI is unavailable.</summary>
    Task<DayPlan> WriteAsync(Guid userId, DateOnly localDate, CancellationToken ct = default);
}
