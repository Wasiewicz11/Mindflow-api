using System.ComponentModel.DataAnnotations;

namespace Mindflow.Api.Models.Dtos;

public record BrainSourceRefDto(
    [MaxLength(40)] string Type,
    [MaxLength(120)] string Id);

public record BrainNodeDto(
    [MaxLength(120)] string Id,
    [MaxLength(240)] string Label,
    [MaxLength(2000)] string? Description,
    double X,
    double Y,
    [MaxLength(40)] string Kind,
    [MaxLength(32)] string Accent,
    BrainSourceRefDto? SourceRef);

public record BrainEdgeDto(
    [MaxLength(120)] string Id,
    [MaxLength(120)] string From,
    [MaxLength(120)] string To,
    [MaxLength(240)] string? Label,
    [MaxLength(40)] string Kind);

public record BrainGraphDto(
    [MaxLength(80)] string Id,
    [MaxLength(160)] string Title,
    int Version,
    IReadOnlyCollection<BrainNodeDto> Nodes,
    IReadOnlyCollection<BrainEdgeDto> Edges);
