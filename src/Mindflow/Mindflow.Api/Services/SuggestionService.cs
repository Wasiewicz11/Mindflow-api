using System.Text.Json;
using Microsoft.Extensions.Options;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Repositories;
using Mindflow.Api.Services.Ai;

namespace Mindflow.Api.Services;

public class SuggestionService(
    IDaySnapshotBuilder snapshotBuilder,
    IAiSuggestionOrchestrator orchestrator,
    ISuggestionRepository repository,
    ISuggestionActionExecutor actionExecutor,
    ICurrentUserService currentUserService,
    IOptions<AiOptions> options,
    ILogger<SuggestionService> logger) : ISuggestionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly AiOptions _options = options.Value;

    public async Task<int> GenerateForUserAsync(Guid userId, CancellationToken ct = default)
    {
        var snapshotResult = await snapshotBuilder.BuildAsync(userId, ct);
        if (snapshotResult.Snapshot.Tasks.Count == 0)
            return 0;

        var (providerName, drafts) = await orchestrator.GenerateAsync(snapshotResult.Snapshot, ct);
        if (drafts.Count == 0)
            return 0;

        await repository.ExpirePendingForUserAsync(userId);

        var now = DateTimeOffset.UtcNow;
        var created = 0;

        foreach (var draft in drafts.Take(_options.MaxSuggestionsPerRun))
        {
            var actions = MapActions(draft.Actions, snapshotResult.RefToTaskId);
            if (actions.Count == 0) continue;

            await repository.AddAsync(new AiSuggestion
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Title = Truncate(draft.Title, 200),
                Body = Truncate(draft.Body, 2000),
                Status = SuggestionStatus.Pending,
                GeneratedForDate = snapshotResult.Snapshot.Today,
                Provider = providerName,
                CreatedAt = now,
                Actions = actions
            });
            created++;
        }

        logger.LogInformation("Utworzono {Count} sugestii dla usera {UserId} (provider {Provider}).",
            created, userId, providerName);
        return created;
    }

    public async Task<IReadOnlyList<SuggestionResponse>> GetPendingAsync()
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        var suggestions = await repository.GetPendingForUserAsync(userId);
        if (suggestions.Count == 0) return [];

        var taskIds = suggestions.SelectMany(s => s.Actions).Select(a => a.TaskId).Distinct().ToList();
        var titles = await repository.GetTaskTitlesAsync(taskIds);

        return suggestions.Select(s => new SuggestionResponse(
            s.Id,
            s.Title,
            s.Body,
            s.GeneratedForDate,
            s.CreatedAt,
            s.Actions
                .OrderBy(a => a.SortOrder)
                .Select(a => new SuggestionActionResponse(
                    a.Id,
                    a.TaskId,
                    titles.GetValueOrDefault(a.TaskId, "—"),
                    a.ActionType,
                    SummarizeAction(a)))
                .ToList())).ToList();
    }

    public async Task<bool> AcceptAsync(Guid suggestionId)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        var suggestion = await repository.GetByIdWithActionsAsync(suggestionId);

        if (suggestion is null || suggestion.UserId != userId || suggestion.Status != SuggestionStatus.Pending)
            return false;

        foreach (var action in suggestion.Actions.OrderBy(a => a.SortOrder))
        {
            try
            {
                await actionExecutor.ExecuteAsync(action);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Akcja {ActionId} sugestii {SuggestionId} nie wykonała się.",
                    action.Id, suggestionId);
            }
        }

        suggestion.Status = SuggestionStatus.Accepted;
        suggestion.DecidedAt = DateTimeOffset.UtcNow;
        await repository.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RejectAsync(Guid suggestionId)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        var suggestion = await repository.GetByIdWithActionsAsync(suggestionId);

        if (suggestion is null || suggestion.UserId != userId || suggestion.Status != SuggestionStatus.Pending)
            return false;

        suggestion.Status = SuggestionStatus.Rejected;
        suggestion.DecidedAt = DateTimeOffset.UtcNow;
        await repository.SaveChangesAsync();
        return true;
    }

    private List<SuggestionAction> MapActions(
        IReadOnlyList<SuggestionActionDraft> drafts,
        IReadOnlyDictionary<int, Guid> refToTaskId)
    {
        var actions = new List<SuggestionAction>();
        var sort = 0;

        foreach (var draft in drafts)
        {
            if (!refToTaskId.TryGetValue(draft.TaskRef, out var taskId))
                continue;

            actions.Add(new SuggestionAction
            {
                Id = Guid.NewGuid(),
                TaskId = taskId,
                ActionType = draft.ActionType,
                Payload = JsonSerializer.Serialize(draft.Payload, JsonOptions),
                SortOrder = sort++
            });
        }

        return actions;
    }

    private static string SummarizeAction(SuggestionAction action)
    {
        var payload = DeserializePayload(action.Payload);
        return action.ActionType switch
        {
            SuggestionActionType.ChangePriority => $"Priorytet → {payload.GetValueOrDefault("priority", "?")}",
            SuggestionActionType.ChangeDueDate => $"Termin → {payload.GetValueOrDefault("date", "?")}",
            _ => action.ActionType.ToString()
        };
    }

    private static Dictionary<string, string> DeserializePayload(string payload)
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

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
