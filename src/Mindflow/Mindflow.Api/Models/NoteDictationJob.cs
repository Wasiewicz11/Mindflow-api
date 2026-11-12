namespace Mindflow.Api.Models;

/// <summary>A dictated note waiting for the background worker; removed once the note is ready.</summary>
public class NoteDictationJob
{
    public Guid NoteId { get; set; }
    public Guid UserId { get; set; }
    public byte[]? Audio { get; set; }
    public string? AudioMimeType { get; set; }
    public string? Text { get; set; }
    public bool Lite { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
