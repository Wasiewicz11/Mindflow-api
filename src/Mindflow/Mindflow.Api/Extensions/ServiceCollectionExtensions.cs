using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Data;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Repositories;
using Mindflow.Api.Services;

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
        var schemes = new List<string>();

        var authBuilder = services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = null;
            options.DefaultChallengeScheme = null;
        });

        authBuilder.AddGoogleJwt(config, schemes);

        services.AddAuthorization(options =>
        {
            options.DefaultPolicy = new AuthorizationPolicyBuilder()
                .AddAuthenticationSchemes([.. schemes])
                .RequireAuthenticatedUser()
                .Build();
        });

        return services;
    }

    private static AuthenticationBuilder AddGoogleJwt(
        this AuthenticationBuilder builder,
        IConfiguration config,
        List<string> schemes)
    {
        const string scheme = "Google";
        schemes.Add(scheme);

        return builder.AddJwtBearer(scheme, options =>
        {
            options.Authority = "https://accounts.google.com";
            options.Audience = config["Google:ClientId"];
            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = context =>
                {
                    if (context.Principal?.Identity is ClaimsIdentity identity &&
                        !identity.HasClaim(c => c.Type == "auth_provider"))
                    {
                        identity.AddClaim(new Claim("auth_provider", nameof(AuthProvider.Google)));
                    }

                    return Task.CompletedTask;
                }
            };
        });
    }

    public static IServiceCollection AddMindflowRepositories(this IServiceCollection services)
    {
        services.AddScoped<ISpaceRepository, SpaceRepository>();
        services.AddScoped<IProjectRepository, ProjectRepository>();
        return services;
    }

    public static IServiceCollection AddMindflowServices(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ISpaceService, SpaceService>();
        services.AddScoped<IProjectService, ProjectService>();
        return services;
    }
}
