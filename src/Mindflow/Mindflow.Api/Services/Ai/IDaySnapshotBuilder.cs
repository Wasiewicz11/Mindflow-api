namespace Mindflow.Api.Services.Ai;

public interface IDaySnapshotBuilder
{
    Task<DaySnapshotResult> BuildAsync(Guid userId, CancellationToken ct = default);
}

public record DaySnapshotResult(DaySnapshot Snapshot, IReadOnlyDictionary<int, Guid> RefToTaskId);
