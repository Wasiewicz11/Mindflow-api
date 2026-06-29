using System.ComponentModel.DataAnnotations;

namespace Mindflow.Api.Models;

public class BrainMap
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    [MaxLength(80)]
    public required string Key { get; set; }

    [MaxLength(160)]
    public required string Title { get; set; }

    public int Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public User? User { get; set; }
    public List<BrainNode> Nodes { get; set; } = new();
    public List<BrainEdge> Edges { get; set; } = new();
}
