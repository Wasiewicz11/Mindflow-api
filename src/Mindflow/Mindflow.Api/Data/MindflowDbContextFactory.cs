using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Mindflow.Api.Data;

/// <summary>Lets EF create migrations without booting hosted workers or external integrations.</summary>
public class MindflowDbContextFactory : IDesignTimeDbContextFactory<MindflowDbContext>
{
    public MindflowDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<MindflowDbContext>()
            // EF only needs a provider to construct the model; no connection is opened here.
            .UseNpgsql("Host=localhost;Database=mindflow_design;Username=postgres;Password=postgres")
            .UseSnakeCaseNamingConvention()
            .Options;
        return new MindflowDbContext(options);
    }
}
