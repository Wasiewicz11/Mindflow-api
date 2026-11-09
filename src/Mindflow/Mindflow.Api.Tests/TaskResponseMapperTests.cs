using Mindflow.Api.Models;
using Mindflow.Api.Services;

namespace Mindflow.Api.Tests;

public class TaskResponseMapperTests
{
    [Fact]
    public void External_reference_is_exposed_in_list_and_detail_responses()
    {
        var task = BuildTask();
        task.ExternalSource = "rendmark";
        task.ExternalId = "task-42";
        task.ExternalUrl = "https://app.rendmark.pl/tasks/42";

        var list = TaskResponseMapper.ToListResponse(task);
        var detail = TaskResponseMapper.ToDetailResponse(task);

        Assert.Equal(("rendmark", "task-42", "https://app.rendmark.pl/tasks/42"), (list.ExternalSource, list.ExternalId, list.ExternalUrl));
        Assert.Equal(("rendmark", "task-42", "https://app.rendmark.pl/tasks/42"), (detail.ExternalSource, detail.ExternalId, detail.ExternalUrl));
    }

    [Fact]
    public void Task_without_external_reference_returns_nulls()
    {
        var detail = TaskResponseMapper.ToDetailResponse(BuildTask());

        Assert.Null(detail.ExternalSource);
        Assert.Null(detail.ExternalId);
        Assert.Null(detail.ExternalUrl);
    }

    private static TaskItem BuildTask() => new()
    {
        Id = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        Content = "Montaż",
        CreatedAt = DateTimeOffset.UtcNow
    };
}
