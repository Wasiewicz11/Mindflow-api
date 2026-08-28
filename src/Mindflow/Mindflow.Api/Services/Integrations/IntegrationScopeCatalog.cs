using Mindflow.Api.Models.Enums;

namespace Mindflow.Api.Services.Integrations;

public record IntegrationScopeDefinition(
    IntegrationTokenScope Scope,
    string Name,
    string Description);

/// <summary>Single source of truth for integration scopes: the public name is what tokens carry and what the docs advertise.</summary>
public static class IntegrationScopeCatalog
{
    private static readonly IntegrationScopeDefinition[] Definitions =
    [
        new(IntegrationTokenScope.ProjectsRead, "projects:read", "Read accessible projects."),
        new(IntegrationTokenScope.TasksRead, "tasks:read", "Read tasks."),
        new(IntegrationTokenScope.TasksCreate, "tasks:create", "Create tasks."),
        new(IntegrationTokenScope.TasksUpdate, "tasks:update", "Update tasks."),
        new(IntegrationTokenScope.TasksDelete, "tasks:delete", "Delete tasks."),
        new(IntegrationTokenScope.SubtasksRead, "subtasks:read", "Read subtasks."),
        new(IntegrationTokenScope.SubtasksCreate, "subtasks:create", "Create subtasks."),
        new(IntegrationTokenScope.SubtasksUpdate, "subtasks:update", "Update subtasks."),
        new(IntegrationTokenScope.SubtasksDelete, "subtasks:delete", "Delete subtasks."),
        new(IntegrationTokenScope.TimeEntriesRead, "time_entries:read", "Read task time entries."),
        new(IntegrationTokenScope.TimeEntriesCreate, "time_entries:create", "Create task time entries."),
        new(IntegrationTokenScope.TimeEntriesUpdate, "time_entries:update", "Update task time entries."),
        new(IntegrationTokenScope.TimeEntriesDelete, "time_entries:delete", "Delete task time entries.")
    ];

    private static readonly Dictionary<IntegrationTokenScope, IntegrationScopeDefinition> ByScope =
        Definitions.ToDictionary(definition => definition.Scope);

    public static IReadOnlyList<IntegrationScopeDefinition> All => Definitions;

    public static string ToName(IntegrationTokenScope scope) =>
        ByScope.TryGetValue(scope, out var definition)
            ? definition.Name
            : throw new ArgumentOutOfRangeException(nameof(scope), scope, "Scope is missing from the catalog.");

    public static bool IsKnown(IntegrationTokenScope scope) => ByScope.ContainsKey(scope);
}
