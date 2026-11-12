using Mindflow.Api.Models.Enums;

namespace Mindflow.Api.Models;

/// <summary>A task the AI found in a dictated note, waiting for the owner's decision.</summary>
public class NoteTaskSuggestion
{
    public Guid Id { get; set; }
    public Guid NoteId { get; set; }
    public int Order { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public DateOnly? DueDate { get; set; }
    public TimeOnly? DueTime { get; set; }
    public TaskPriority Priority { get; set; }
    public Guid? ProjectId { get; set; }
    public List<string> Subtasks { get; set; } = new();
    public NoteSuggestionStatus Status { get; set; }
    public Guid? TaskId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
