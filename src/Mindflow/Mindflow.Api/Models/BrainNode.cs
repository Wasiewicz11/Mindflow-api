using System.ComponentModel.DataAnnotations;

namespace Mindflow.Api.Models;

public class BrainNode
{
    public Guid Id { get; set; }
    public Guid BrainMapId { get; set; }

    [MaxLength(120)]
    public required string Key { get; set; }

    [MaxLength(240)]
    public required string Label { get; set; }

    [MaxLength(2000)]
    public string? Description { get; set; }

    public double X { get; set; }
    public double Y { get; set; }

    [MaxLength(40)]
    public required string Kind { get; set; }

    [MaxLength(32)]
    public required string Accent { get; set; }

    [MaxLength(40)]
    public string? SourceType { get; set; }

    [MaxLength(120)]
    public string? SourceId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public BrainMap? BrainMap { get; set; }
}
