using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Mindflow.Api.Data;

/// <summary>Lets EF create migrations without booting hosted workers or external integrations.</summary>
public class MindflowDbContextFactory : IDesignTimeDbContextFactory<MindflowDbContext>
{
    public MindflowDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Database")
            ?? "Host=localhost;Database=mindflow_design;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<MindflowDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new MindflowDbContext(options);
    }
}
