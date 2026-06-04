using Mindflow.Api.Exceptions;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Repositories;

namespace Mindflow.Api.Services;

public class ProjectTagService(
    IProjectTagRepository projectTagRepository,
    ICurrentUserService currentUserService,
    IAccessService accessService) : IProjectTagService
{
    public async Task<IReadOnlyList<string>> GetForProjectAsync(Guid projectId)
    {
        await EnsureProjectAccessAsync(projectId);
        return await projectTagRepository.GetNamesForProjectAsync(projectId);
    }

    public async Task<IReadOnlyList<string>> CreateAsync(Guid projectId, ProjectTagRequest request)
    {
        await EnsureProjectAccessAsync(projectId);
        var name = NormalizeName(request.Name);

        await projectTagRepository.EnsureExistAsync(projectId, new[] { name });
        return await projectTagRepository.GetNamesForProjectAsync(projectId);
    }

    public async Task<IReadOnlyList<string>> RenameAsync(Guid projectId, string currentName, ProjectTagRequest request)
    {
        await EnsureProjectAccessAsync(projectId);
        var from = NormalizeName(currentName);
        var to = NormalizeName(request.Name);

        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
        {
            await projectTagRepository.EnsureExistAsync(projectId, new[] { to });
            return await projectTagRepository.GetNamesForProjectAsync(projectId);
        }

        return await projectTagRepository.RenameAsync(projectId, from, to);
    }

    public async Task<IReadOnlyList<string>> DeleteAsync(Guid projectId, string name)
    {
        await EnsureProjectAccessAsync(projectId);
        return await projectTagRepository.DeleteAsync(projectId, NormalizeName(name));
    }

    private async Task EnsureProjectAccessAsync(Guid projectId)
    {
        var userId = await currentUserService.GetCurrentUserIdAsync();
        if (!await accessService.CanAccessProjectAsync(projectId, userId))
        {
            throw new ForbiddenException("Access to this project is denied.");
        }
    }

    private static string NormalizeName(string name)
    {
        var normalized = name.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new BadRequestException("Tag name is required.");
        }

        if (normalized.Length > 50)
        {
            throw new BadRequestException("Tag name cannot be longer than 50 characters.");
        }

        return normalized;
    }
}
