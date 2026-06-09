using System.Security.Claims;
using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Data;
using Mindflow.Api.Hubs;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Repositories;
using Mindflow.Api.Services;
using Mindflow.Api.Services.Ai;
using Mindflow.Api.Services.GoogleCalendar;

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
            options.DefaultAuthenticateScheme = "Mindflow";
            options.DefaultChallengeScheme = "Mindflow";
        });

        authBuilder.AddGoogleJwt(config, schemes);
        authBuilder.AddMindflowJwt(config, schemes);

        services.AddAuthorization(options =>
        {
            options.DefaultPolicy = new AuthorizationPolicyBuilder()
                .AddAuthenticationSchemes([.. schemes])
                .RequireAuthenticatedUser()
                .Build();
        });

        return services;
    }

    private static AuthenticationBuilder AddMindflowJwt(
        this AuthenticationBuilder builder,
        IConfiguration config,
        List<string> schemes)
    {
        const string scheme = "Mindflow";
        schemes.Add(scheme);

        return builder.AddJwtBearer(scheme, options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = config["Jwt:Issuer"],
                ValidateAudience = true,
                ValidAudience = config["Jwt:Audience"],
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(config["Jwt:Secret"]
                        ?? throw new InvalidOperationException("Jwt:Secret is not configured."))),
                ValidateLifetime = true
            };
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var accessToken = context.Request.Query["access_token"];
                    var path = context.HttpContext.Request.Path;

                    if (!string.IsNullOrEmpty(accessToken) &&
                        path.StartsWithSegments("/hubs"))
                    {
                        context.Token = accessToken;
                    }

                    return Task.CompletedTask;
                }
            };
        });
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
            var audiences = new List<string>();
            var webClientId = config["Google:ClientId"];
            var iosClientId = config["Google:IosClientId"];
            if (!string.IsNullOrWhiteSpace(webClientId)) audiences.Add(webClientId);
            if (!string.IsNullOrWhiteSpace(iosClientId)) audiences.Add(iosClientId);
            options.TokenValidationParameters.ValidAudiences = audiences;

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
                },
                OnMessageReceived = context =>
                {
                    var accessToken = context.Request.Query["access_token"];
                    var path = context.HttpContext.Request.Path;

                    if (!string.IsNullOrEmpty(accessToken) &&
                        path.StartsWithSegments("/hubs"))
                    {
                        context.Token = accessToken;
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
        services.AddScoped<IProjectTagRepository, ProjectTagRepository>();
        services.AddScoped<ITaskRepository, TaskRepository>();
        services.AddScoped<ITaskSubtaskRepository, TaskSubtaskRepository>();
        services.AddScoped<ICalendarBlockRepository, CalendarBlockRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        return services;
    }

    public static IServiceCollection AddMindflowServices(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IAccessService, AccessService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IStorageService, SupabaseStorageService>();
        services.AddScoped<ISpaceService, SpaceService>();
        services.AddScoped<IProjectService, ProjectService>();
        services.AddScoped<IProjectTagService, ProjectTagService>();
        services.AddScoped<ITaskService, TaskService>();
        services.AddScoped<ITaskSubtaskService, TaskSubtaskService>();
        services.AddScoped<ITaskActivityService, TaskActivityService>();
        services.AddScoped<ICalendarBlockService, CalendarBlockService>();
        services.AddScoped<ITasksNotifier, TasksNotifier>();
        services.AddScoped<TokenService>();
        return services;
    }

    public static IServiceCollection AddMindflowStorage(this IServiceCollection services, IConfiguration config)
    {
        var serviceUrl = config["SupabaseStorage:ServiceUrl"]
            ?? throw new InvalidOperationException("SupabaseStorage:ServiceUrl is not configured.");
        var accessKey = config["SupabaseStorage:AccessKey"]
            ?? throw new InvalidOperationException("SupabaseStorage:AccessKey is not configured.");
        var secretKey = config["SupabaseStorage:SecretKey"]
            ?? throw new InvalidOperationException("SupabaseStorage:SecretKey is not configured.");

        var s3Config = new AmazonS3Config
        {
            ServiceURL = serviceUrl,
            ForcePathStyle = true
        };

        var credentials = new BasicAWSCredentials(accessKey, secretKey);
        services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client(credentials, s3Config));
        services.AddHttpClient();

        return services;
    }

    public static IServiceCollection AddMindflowGoogleCalendar(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<GoogleCalendarOptions>(config.GetSection(GoogleCalendarOptions.SectionName));

        services.AddSingleton<IGoogleTokenProtector, GoogleTokenProtector>();
        services.AddSingleton<IOAuthStateProtector, OAuthStateProtector>();
        services.AddScoped<IGoogleCalendarConnectionRepository, GoogleCalendarConnectionRepository>();
        services.AddScoped<IGoogleCalendarClient, GoogleCalendarClient>();
        services.AddScoped<IGoogleCalendarSyncService, GoogleCalendarSyncService>();
        services.AddScoped<IGoogleCalendarConnectionService, GoogleCalendarConnectionService>();

        return services;
    }

    public static IServiceCollection AddMindflowAi(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<AiOptions>(config.GetSection(AiOptions.SectionName));

        services.AddScoped<IDaySnapshotBuilder, DaySnapshotBuilder>();
        services.AddScoped<ISuggestionRepository, SuggestionRepository>();
        services.AddScoped<IAiUsageRepository, AiUsageRepository>();
        services.AddScoped<ISuggestionActionExecutor, SuggestionActionExecutor>();
        services.AddScoped<IAiSuggestionOrchestrator, AiSuggestionOrchestrator>();
        services.AddScoped<ISuggestionService, SuggestionService>();

        services.AddHttpClient<GeminiSuggestionProvider>();
        services.AddHttpClient<OpenAiSuggestionProvider>();
        services.AddScoped<IAiSuggestionProvider>(sp => sp.GetRequiredService<GeminiSuggestionProvider>());
        services.AddScoped<IAiSuggestionProvider>(sp => sp.GetRequiredService<OpenAiSuggestionProvider>());
        services.AddScoped<IAiSuggestionProvider, RuleBasedSuggestionProvider>();

        return services;
    }
}
