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
        var existing = await db.BrainMaps
            .Include(map => map.Nodes)
            .Include(map => map.Edges)
            .FirstOrDefaultAsync(map => map.UserId == userId && map.Key == DefaultKey);

        if (existing is null)
        {
            db.BrainMaps.Add(graph);
            await db.SaveChangesAsync();
            return graph;
        }

        db.BrainEdges.RemoveRange(existing.Edges);
        db.BrainNodes.RemoveRange(existing.Nodes);

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

        existing.Nodes = graph.Nodes;
        existing.Edges = graph.Edges;
        return existing;
    }
}
