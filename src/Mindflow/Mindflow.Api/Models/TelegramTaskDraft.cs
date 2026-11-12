using Mindflow.Api.Models.Enums;

namespace Mindflow.Api.Models;

/// <summary>Tasks recognized from a Telegram message, waiting for the user's decision.</summary>
public class TelegramTaskDraft
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public long SourceChatId { get; set; }
    public long SourceMessageId { get; set; }
    public string Tasks { get; set; } = "[]";
    public TelegramTaskDraftStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
