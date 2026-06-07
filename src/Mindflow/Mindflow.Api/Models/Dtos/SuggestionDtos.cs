using Mindflow.Api.Models.Enums;

namespace Mindflow.Api.Models.Dtos;

public record SuggestionResponse(
    Guid Id,
    string Title,
    string Body,
    DateOnly GeneratedForDate,
    DateTimeOffset CreatedAt,
    IReadOnlyList<SuggestionActionResponse> Actions);

public record SuggestionActionResponse(
    Guid Id,
    Guid TaskId,
    string TaskTitle,
    SuggestionActionType ActionType,
    string Summary);
