using Mindflow.Api.Models.Enums;
using Mindflow.Api.Services.Integrations;

namespace Mindflow.Api.Tests;

public class IntegrationScopeCatalogTests
{
    [Fact]
    public void Every_scope_has_a_catalog_entry()
    {
        var missing = Enum.GetValues<IntegrationTokenScope>()
            .Where(scope => !IntegrationScopeCatalog.IsKnown(scope))
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void Public_names_are_unique()
    {
        var names = IntegrationScopeCatalog.All.Select(definition => definition.Name).ToList();

        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Public_names_use_the_documented_shape()
    {
        Assert.All(IntegrationScopeCatalog.All, definition =>
        {
            Assert.Contains(':', definition.Name);
            Assert.Equal(definition.Name.ToLowerInvariant(), definition.Name);
            Assert.False(string.IsNullOrWhiteSpace(definition.Description));
        });
    }

    [Fact]
    public void ToName_round_trips_through_the_catalog()
    {
        foreach (var definition in IntegrationScopeCatalog.All)
        {
            Assert.Equal(definition.Name, IntegrationScopeCatalog.ToName(definition.Scope));
        }
    }

    [Theory]
    [InlineData("TasksRead", IntegrationTokenScope.TasksRead)]
    [InlineData("tasks:read", IntegrationTokenScope.TasksRead)]
    [InlineData("time_entries:delete", IntegrationTokenScope.TimeEntriesDelete)]
    [InlineData("spaces:read", IntegrationTokenScope.SpacesRead)]
    public void Scope_parses_from_enum_or_public_name(string value, IntegrationTokenScope expected)
    {
        Assert.True(IntegrationScopeCatalog.TryParse(value, out var scope));
        Assert.Equal(expected, scope);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("tasksread")]
    [InlineData("")]
    public void Unknown_or_numeric_scope_does_not_parse(string value)
    {
        Assert.False(IntegrationScopeCatalog.TryParse(value, out _));
    }
}
