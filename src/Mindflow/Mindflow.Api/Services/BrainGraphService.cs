using Mindflow.Api.Exceptions;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Repositories;

namespace Mindflow.Api.Services;

public class BrainGraphService(
    IBrainGraphRepository brainGraphRepository,
    ICurrentUserService currentUserService) : IBrainGraphService
{
    private const string DefaultKey = "personal-goals";
    private const int MaxNodes = 250;
    private const int MaxEdges = 600;

    public async Task<BrainGraphDto?> GetDefaultAsync()
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        var graph = await brainGraphRepository.GetDefaultAsync(userId);
        return graph is null ? null : ToDto(graph);
    }

    public async Task<BrainGraphDto> UpsertDefaultAsync(BrainGraphDto request)
    {
        Validate(request);

        var userId = await currentUserService.GetCurrentUserIdAsync();
        var now = DateTimeOffset.UtcNow;
        var graph = new BrainMap
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Key = DefaultKey,
            Title = string.IsNullOrWhiteSpace(request.Title) ? "Brain" : request.Title.Trim(),
            Version = request.Version <= 0 ? 1 : request.Version,
            CreatedAt = now,
            UpdatedAt = now,
            Nodes = request.Nodes.Select(node => ToEntity(node, now)).ToList(),
            Edges = request.Edges.Select(edge => ToEntity(edge, now)).ToList()
        };

        var saved = await brainGraphRepository.UpsertDefaultAsync(userId, graph);
        return ToDto(saved);
    }

    private static void Validate(BrainGraphDto graph)
    {
        if (graph is null)
            throw new BadRequestException("Brain graph payload is required.");

        if (graph.Nodes is null)
            throw new BadRequestException("Brain graph nodes are required.");

        if (graph.Edges is null)
            throw new BadRequestException("Brain graph edges are required.");

        if (graph.Nodes.Count > MaxNodes)
            throw new BadRequestException($"Brain graph can contain at most {MaxNodes} nodes.");

        if (graph.Edges.Count > MaxEdges)
            throw new BadRequestException($"Brain graph can contain at most {MaxEdges} edges.");

        var nodeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in graph.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Id))
                throw new BadRequestException("Brain node id is required.");
            if (string.IsNullOrWhiteSpace(node.Label))
                throw new BadRequestException("Brain node label is required.");
            if (string.IsNullOrWhiteSpace(node.Kind))
                throw new BadRequestException("Brain node kind is required.");
            if (string.IsNullOrWhiteSpace(node.Accent))
                throw new BadRequestException("Brain node accent is required.");
            if (!double.IsFinite(node.X) || !double.IsFinite(node.Y))
                throw new BadRequestException("Brain node coordinates must be finite numbers.");
            if (!nodeIds.Add(node.Id))
                throw new BadRequestException("Brain node ids must be unique.");
        }

        var edgeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var edge in graph.Edges)
        {
            if (string.IsNullOrWhiteSpace(edge.Id))
                throw new BadRequestException("Brain edge id is required.");
            if (string.IsNullOrWhiteSpace(edge.From) || string.IsNullOrWhiteSpace(edge.To))
                throw new BadRequestException("Brain edge endpoints are required.");
            if (string.IsNullOrWhiteSpace(edge.Kind))
                throw new BadRequestException("Brain edge kind is required.");
            if (!nodeIds.Contains(edge.From) || !nodeIds.Contains(edge.To))
                throw new BadRequestException("Brain edge endpoints must reference existing nodes.");
            if (!edgeIds.Add(edge.Id))
                throw new BadRequestException("Brain edge ids must be unique.");
        }
    }

    private static BrainNode ToEntity(BrainNodeDto node, DateTimeOffset now)
    {
        return new BrainNode
        {
            Id = Guid.NewGuid(),
            Key = node.Id.Trim(),
            Label = node.Label.Trim(),
            Description = string.IsNullOrWhiteSpace(node.Description) ? null : node.Description.Trim(),
            X = node.X,
            Y = node.Y,
            Kind = node.Kind.Trim(),
            Accent = node.Accent.Trim(),
            SourceType = string.IsNullOrWhiteSpace(node.SourceRef?.Type) ? null : node.SourceRef.Type.Trim(),
            SourceId = string.IsNullOrWhiteSpace(node.SourceRef?.Id) ? null : node.SourceRef.Id.Trim(),
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static BrainEdge ToEntity(BrainEdgeDto edge, DateTimeOffset now)
    {
        return new BrainEdge
        {
            Id = Guid.NewGuid(),
            Key = edge.Id.Trim(),
            FromNodeKey = edge.From.Trim(),
            ToNodeKey = edge.To.Trim(),
            Label = string.IsNullOrWhiteSpace(edge.Label) ? null : edge.Label.Trim(),
            Kind = edge.Kind.Trim(),
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static BrainGraphDto ToDto(BrainMap graph)
    {
        return new BrainGraphDto(
            graph.Key,
            graph.Title,
            graph.Version,
            graph.Nodes
                .OrderBy(node => node.CreatedAt)
                .Select(ToDto)
                .ToList(),
            graph.Edges
                .OrderBy(edge => edge.CreatedAt)
                .Select(ToDto)
                .ToList());
    }

    private static BrainNodeDto ToDto(BrainNode node)
    {
        var sourceRef = node.SourceType is null || node.SourceId is null
            ? null
            : new BrainSourceRefDto(node.SourceType, node.SourceId);

        return new BrainNodeDto(
            node.Key,
            node.Label,
            node.Description,
            node.X,
            node.Y,
            node.Kind,
            node.Accent,
            sourceRef);
    }

    private static BrainEdgeDto ToDto(BrainEdge edge)
    {
        return new BrainEdgeDto(
            edge.Key,
            edge.FromNodeKey,
            edge.ToNodeKey,
            edge.Label,
            edge.Kind);
    }
}
