using System.ComponentModel.DataAnnotations;
using Mindflow.Api.Models.Enums;

namespace Mindflow.Api.Models;

public class AiSuggestion
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    [MaxLength(200)]
    public required string Title { get; set; }

    [MaxLength(2000)]
    public required string Body { get; set; }

    public SuggestionStatus Status { get; set; }

    public DateOnly GeneratedForDate { get; set; }

    [MaxLength(50)]
    public string? Provider { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    public List<SuggestionAction> Actions { get; set; } = new();
}
