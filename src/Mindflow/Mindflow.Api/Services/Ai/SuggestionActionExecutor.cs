using System.Globalization;
using System.Text.Json;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Services;

namespace Mindflow.Api.Services.Ai;

public class SuggestionActionExecutor(
    ITaskService taskService,
    ILogger<SuggestionActionExecutor> logger) : ISuggestionActionExecutor
{
    public async Task ExecuteAsync(SuggestionAction action, CancellationToken ct = default)
    {
        var payload = ParsePayload(action.Payload);
        var request = BuildRequest(action.ActionType, payload);

        if (request is null)
        {
            logger.LogWarning("Pominięto akcję {Type} dla zadania {TaskId} — niepoprawny payload.",
                action.ActionType, action.TaskId);
            return;
        }

        await taskService.UpdateAsync(action.TaskId, request);
    }

    private static UpdateTaskRequest? BuildRequest(SuggestionActionType type, IReadOnlyDictionary<string, string> payload)
    {
        switch (type)
        {
            case SuggestionActionType.ChangePriority:
                if (payload.TryGetValue("priority", out var raw)
                    && Enum.TryParse<TaskPriority>(raw, ignoreCase: true, out var priority))
                {
                    return new UpdateTaskRequest(null, null, priority, null, null, false, null, false, null, null);
                }
                return null;

            case SuggestionActionType.ChangeDueDate:
                if (payload.TryGetValue("date", out var dateRaw)
                    && DateOnly.TryParse(dateRaw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    return new UpdateTaskRequest(null, null, null, null, date, false, null, false, null, null);
                }
                return null;

            default:
                return null;
        }
    }

    private static Dictionary<string, string> ParsePayload(string payload)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(payload) ?? new();
        }
        catch (JsonException)
        {
            return new();
        }
    }
}
