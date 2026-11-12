using Mindflow.Api.Models.Enums;

namespace Mindflow.Api.Models;

/// <summary>A private markdown note; dictated notes stay "new" until the owner opens them.</summary>
public class Note
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string? ClientId { get; set; }
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public List<string> Tags { get; set; } = new();
    public Guid? ProjectId { get; set; }
    public NoteSource Source { get; set; }
    public NoteStatus Status { get; set; }
    public string? Transcript { get; set; }
    public DateOnly? EventDate { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
