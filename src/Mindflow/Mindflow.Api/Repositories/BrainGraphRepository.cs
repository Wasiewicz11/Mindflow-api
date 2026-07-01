using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Data;
using Mindflow.Api.Models;

namespace Mindflow.Api.Repositories;

public class BrainGraphRepository(MindflowDbContext db) : IBrainGraphRepository
{
    private const string DefaultKey = "personal-goals";

    public async Task<BrainMap?> GetDefaultAsync(Guid userId)
    {
        return await db.BrainMaps
            .AsNoTracking()
            .Include(map => map.Nodes)
            .Include(map => map.Edges)
            .FirstOrDefaultAsync(map => map.UserId == userId && map.Key == DefaultKey);
    }

    public async Task<BrainMap> UpsertDefaultAsync(Guid userId, BrainMap graph)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();

        var existing = await db.BrainMaps
            .Include(map => map.Nodes)
            .Include(map => map.Edges)
            .FirstOrDefaultAsync(map => map.UserId == userId && map.Key == DefaultKey);

        if (existing is null)
        {
            foreach (var node in graph.Nodes)
                node.BrainMapId = graph.Id;

            foreach (var edge in graph.Edges)
                edge.BrainMapId = graph.Id;

            db.BrainMaps.Add(graph);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return graph;
        }

        db.BrainEdges.RemoveRange(existing.Edges);
        db.BrainNodes.RemoveRange(existing.Nodes);
        await db.SaveChangesAsync();

        existing.Title = graph.Title;
        existing.Version = graph.Version;
        existing.UpdatedAt = graph.UpdatedAt;

        foreach (var node in graph.Nodes)
        {
            node.BrainMapId = existing.Id;
            db.BrainNodes.Add(node);
        }

        foreach (var edge in graph.Edges)
        {
            edge.BrainMapId = existing.Id;
            db.BrainEdges.Add(edge);
        }

        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        existing.Nodes = graph.Nodes;
        existing.Edges = graph.Edges;
        return existing;
    }
}
