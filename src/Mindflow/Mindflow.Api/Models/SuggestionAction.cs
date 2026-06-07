using Mindflow.Api.Models.Enums;

namespace Mindflow.Api.Models;

public class SuggestionAction
{
    public Guid Id { get; set; }
    public Guid SuggestionId { get; set; }
    public Guid TaskId { get; set; }
    public SuggestionActionType ActionType { get; set; }
    public string Payload { get; set; } = "{}";
    public int SortOrder { get; set; }
}
