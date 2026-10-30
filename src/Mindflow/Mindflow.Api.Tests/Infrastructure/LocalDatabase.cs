using Npgsql;

namespace Mindflow.Api.Tests.Infrastructure;

public static class LocalDatabase
{
    private const string EnvironmentVariable = "ConnectionStrings__Database";
    private static readonly string[] LocalHosts = ["localhost", "127.0.0.1", "::1"];

    public static string? ConnectionString { get; } = Resolve();

    public static string SkipReason =>
        $"Set {EnvironmentVariable} to a local PostgreSQL database to run API tests.";

    private static string? Resolve()
    {
        var value = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(value)) return null;

        var builder = new NpgsqlConnectionStringBuilder(value);
        return LocalHosts.Contains(builder.Host, StringComparer.OrdinalIgnoreCase) ? value : null;
    }
}

public sealed class LocalDatabaseFactAttribute : FactAttribute
{
    public LocalDatabaseFactAttribute()
    {
        if (LocalDatabase.ConnectionString is null) Skip = LocalDatabase.SkipReason;
    }
}
