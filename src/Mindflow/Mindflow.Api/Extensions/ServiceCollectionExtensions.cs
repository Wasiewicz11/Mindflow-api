using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Data;

namespace Mindflow.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMindflowDatabase(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<MindflowDbContext>(opt =>
            opt.UseNpgsql(config.GetConnectionString("Database"))
               .UseSnakeCaseNamingConvention());
        return services;
    }

    public static IServiceCollection AddMindflowAuth(this IServiceCollection services, IConfiguration config)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                var supabaseUrl = config["Supabase:Url"];
                options.Authority = $"{supabaseUrl}/auth/v1";
                options.Audience = config["Supabase:JwtAudience"];
                options.TokenValidationParameters.ValidIssuer = $"{supabaseUrl}/auth/v1";
            });
        services.AddAuthorization();
        return services;
    }

    public static IServiceCollection AddMindflowRepositories(this IServiceCollection services)
    {
        return services;
    }

    public static IServiceCollection AddMindflowServices(this IServiceCollection services)
    {
        return services;
    }
}
