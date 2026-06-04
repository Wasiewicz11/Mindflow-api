namespace Mindflow.Api.Repositories;

public interface IProjectTagRepository
{
    Task<IReadOnlyList<string>> GetNamesForProjectAsync(Guid projectId);

    /// <summary>
    /// Ensures every name in <paramref name="names"/> exists in the project's tag pool
    /// (case-insensitive match against existing names). Returns the canonical names
    /// in the order they were requested — using the existing pool entry's casing if a
    /// case-insensitive match was found, otherwise the trimmed input.
    /// </summary>
    Task<IReadOnlyList<string>> EnsureExistAsync(Guid projectId, IReadOnlyCollection<string> names);

    Task<IReadOnlyList<string>> RenameAsync(Guid projectId, string currentName, string newName);

    Task<IReadOnlyList<string>> DeleteAsync(Guid projectId, string name);
}
