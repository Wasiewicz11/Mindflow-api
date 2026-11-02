using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Mindflow.Api.Data;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Models.Enums;

namespace Mindflow.Api.Tests.Infrastructure;

public class MindflowApiFactory : WebApplicationFactory<Program>
{
    private const string JwtSecret = "mindflow-tests-jwt-secret-0123456789abcdef";
    private const string JwtIssuer = "mindflow-api";
    private const string JwtAudience = "mindflow-ui";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly List<Guid> createdUserIds = [];

    protected virtual IReadOnlyDictionary<string, string> Settings => new Dictionary<string, string>
    {
        ["ConnectionStrings:Database"] = LocalDatabase.ConnectionString ?? "",
        ["Jwt:Secret"] = JwtSecret,
        ["Jwt:Issuer"] = JwtIssuer,
        ["Jwt:Audience"] = JwtAudience,
        ["IntegrationTokens:HashPepper"] = "mindflow-tests-integration-pepper-0123456789",
        ["IntegrationTokens:OAuthRateLimitPermitsPerMinute"] = OAuthRateLimitPermitsPerMinute.ToString(),
        ["Integrations:Clients:rendmark:Name"] = "Rendmark",
        ["Integrations:Clients:rendmark:SecretHash"] = Sha256Hex(ClientSecret),
        ["Integrations:Clients:rendmark:RedirectUris:0"] = RedirectUri,
        ["Integrations:Clients:rendmark:TokenLifetimeDays"] = "365"
    };

    protected virtual int OAuthRateLimitPermitsPerMinute => 1000;

    public const string ClientId = "rendmark";
    public const string ClientSecret = "mindflow-tests-client-secret";
    public const string RedirectUri = "https://rendmark.test/integrations/mindflow/callback";

    public static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        foreach (var (key, value) in Settings)
        {
            builder.UseSetting(key, value);
        }
    }

    public async Task<User> CreateUserAsync(bool integrationsEnabled = true)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = $"test-{Guid.NewGuid():N}"[..30] + "@example.com",
            FirstName = "Test",
            LastName = "User",
            TimeZone = "Europe/Warsaw",
            IntegrationsEnabled = integrationsEnabled,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await WithDbAsync(async db =>
        {
            db.Users.Add(user);
            await db.SaveChangesAsync();
        });

        lock (createdUserIds) createdUserIds.Add(user.Id);
        return user;
    }

    public async Task<Project> CreateProjectAsync(Guid userId, string name = "Projekt testowy")
    {
        var project = new Project
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = name,
            Color = "#000000",
            CreatedAt = DateTimeOffset.UtcNow
        };

        await WithDbAsync(async db =>
        {
            db.Projects.Add(project);
            await db.SaveChangesAsync();
        });

        return project;
    }

    public HttpClient CreateUserClient(Guid userId)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateUserJwt(userId));
        return client;
    }

    public HttpClient CreateIntegrationClient(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public async Task<string> CreateManualTokenAsync(Guid userId, params IntegrationTokenScope[] scopes)
    {
        using var client = CreateUserClient(userId);
        var response = await client.PostAsJsonAsync(
            "/integrations/tokens",
            new CreateIntegrationTokenRequest("Testowy klucz", scopes, DateTimeOffset.UtcNow.AddDays(30)),
            Json);
        response.EnsureSuccessStatusCode();

        var created = await response.Content.ReadFromJsonAsync<CreateIntegrationTokenResponse>(Json);
        return created!.Token;
    }

    public async Task WithDbAsync(Func<MindflowDbContext, Task> action)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MindflowDbContext>();
        await action(db);
    }

    public async Task<T> WithDbAsync<T>(Func<MindflowDbContext, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MindflowDbContext>();
        return await action(db);
    }

    public override async ValueTask DisposeAsync()
    {
        Guid[] userIds;
        lock (createdUserIds) userIds = [.. createdUserIds];

        if (userIds.Length > 0)
        {
            await WithDbAsync(async db =>
            {
                await db.CalendarBlocks.Where(block => userIds.Contains(block.UserId)).ExecuteDeleteAsync();
                await db.Tasks.Where(task => userIds.Contains(task.UserId)).ExecuteDeleteAsync();
                await db.Projects.Where(project => userIds.Contains(project.UserId)).ExecuteDeleteAsync();
                await db.Users.Where(user => userIds.Contains(user.Id)).ExecuteDeleteAsync();
            });
        }

        await base.DisposeAsync();
    }

    private static string CreateUserJwt(Guid userId)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret));
        var token = new JwtSecurityToken(
            issuer: JwtIssuer,
            audience: JwtAudience,
            claims: [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
