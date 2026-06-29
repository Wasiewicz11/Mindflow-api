using System.ComponentModel.DataAnnotations;

namespace Mindflow.Api.Models;

public class BrainEdge
{
    public Guid Id { get; set; }
    public Guid BrainMapId { get; set; }

    [MaxLength(120)]
    public required string Key { get; set; }

    [MaxLength(120)]
    public required string FromNodeKey { get; set; }

    [MaxLength(120)]
    public required string ToNodeKey { get; set; }

    [MaxLength(240)]
    public string? Label { get; set; }

    [MaxLength(40)]
    public required string Kind { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public BrainMap? BrainMap { get; set; }
}
