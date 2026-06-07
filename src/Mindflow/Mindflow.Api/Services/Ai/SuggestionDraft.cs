using Mindflow.Api.Models.Enums;

namespace Mindflow.Api.Services.Ai;

public record SuggestionDraft(
    string Title,
    string Body,
    IReadOnlyList<SuggestionActionDraft> Actions);

public record SuggestionActionDraft(
    int TaskRef,
    SuggestionActionType ActionType,
    IReadOnlyDictionary<string, string> Payload);
