using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Data;
using Mindflow.Api.Models.Enums;
using TaskStatus = Mindflow.Api.Models.Enums.TaskStatus;

namespace Mindflow.Api.Services.Ai;

public class DaySnapshotBuilder(MindflowDbContext db) : IDaySnapshotBuilder
{
    private const int MaxTasks = 40;

    public async Task<DaySnapshotResult> BuildAsync(Guid userId, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var tasks = await db.Tasks
            .AsNoTracking()
            .Where(t => t.UserId == userId && !t.IsCompleted && t.Status != TaskStatus.Completed)
            .OrderBy(t => t.DueDate == null)
            .ThenBy(t => t.DueDate)
            .ThenByDescending(t => t.CreatedAt)
            .Take(MaxTasks)
            .ToListAsync(ct);

        var taskIds = tasks.Select(t => t.Id).ToList();

        var postponeCounts = await db.TaskActivityEvents
            .AsNoTracking()
            .Where(e => e.UserId == userId
                        && e.EventType == TaskActivityEventType.TaskPostponed
                        && e.TaskId != null
                        && taskIds.Contains(e.TaskId.Value))
            .GroupBy(e => e.TaskId!.Value)
            .Select(g => new { TaskId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TaskId, x => x.Count, ct);

        var snapshotTasks = new List<SnapshotTask>(tasks.Count);
        var refToTaskId = new Dictionary<int, Guid>(tasks.Count);
        var nextRef = 1;

        foreach (var t in tasks)
        {
            var reference = nextRef++;
            refToTaskId[reference] = t.Id;

            int? daysUntilDue = t.DueDate.HasValue
                ? t.DueDate.Value.DayNumber - today.DayNumber
                : null;

            var createdDate = DateOnly.FromDateTime(t.CreatedAt.UtcDateTime);

            snapshotTasks.Add(new SnapshotTask(
                Ref: reference,
                Title: t.Content,
                Priority: t.Priority.ToString(),
                Status: t.Status.ToString(),
                HasDueDate: t.DueDate.HasValue,
                DaysUntilDue: daysUntilDue,
                AgeDays: Math.Max(0, today.DayNumber - createdDate.DayNumber),
                TimesPostponed: postponeCounts.GetValueOrDefault(t.Id, 0)));
        }

        return new DaySnapshotResult(new DaySnapshot(today, snapshotTasks), refToTaskId);
    }
}
