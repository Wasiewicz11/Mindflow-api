using Mindflow.Api.Models;

namespace Mindflow.Api.Repositories;

public interface IBrainGraphRepository
{
    Task<BrainMap?> GetDefaultAsync(Guid userId);
    Task<BrainMap> UpsertDefaultAsync(Guid userId, BrainMap graph);
}
