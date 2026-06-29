using System.ComponentModel.DataAnnotations;

namespace Mindflow.Api.Models.Dtos;

public record BrainSourceRefDto(
    [property: MaxLength(40)] string Type,
    [property: MaxLength(120)] string Id);

public record BrainNodeDto(
    [property: MaxLength(120)] string Id,
    [property: MaxLength(240)] string Label,
    [property: MaxLength(2000)] string? Description,
    double X,
    double Y,
    [property: MaxLength(40)] string Kind,
    [property: MaxLength(32)] string Accent,
    BrainSourceRefDto? SourceRef);

public record BrainEdgeDto(
    [property: MaxLength(120)] string Id,
    [property: MaxLength(120)] string From,
    [property: MaxLength(120)] string To,
    [property: MaxLength(240)] string? Label,
    [property: MaxLength(40)] string Kind);

public record BrainGraphDto(
    [property: MaxLength(80)] string Id,
    [property: MaxLength(160)] string Title,
    int Version,
    IReadOnlyCollection<BrainNodeDto> Nodes,
    IReadOnlyCollection<BrainEdgeDto> Edges);
