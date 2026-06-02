using Mindflow.Api.Models.Dtos;

namespace Mindflow.Api.Services;

public interface IProjectTagService
{
    Task<IReadOnlyList<string>> GetForProjectAsync(Guid projectId);
    Task<IReadOnlyList<string>> CreateAsync(Guid projectId, ProjectTagRequest request);
    Task<IReadOnlyList<string>> RenameAsync(Guid projectId, string currentName, ProjectTagRequest request);
    Task<IReadOnlyList<string>> DeleteAsync(Guid projectId, string name);
}
