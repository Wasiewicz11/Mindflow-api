using Mindflow.Api.Models.Dtos;

namespace Mindflow.Api.Services.Ai.Dictation;

public static class DictatedTaskRequests
{
    public static CreateTaskRequest ToCreateRequest(DictatedTaskResponse task) => new(
        task.Title,
        task.Description,
        task.Priority,
        null,
        task.DueDate,
        task.DueDate is null ? null : task.DueTime,
        null,
        task.ProjectId,
        null,
        task.Subtasks
            .Select((content, index) => new TaskSubtaskRequest(null, content, false, null, null, false, null, null, index))
            .ToList());
}
