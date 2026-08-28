using Mindflow.Api.Models.Enums;
using TaskStatus = Mindflow.Api.Models.Enums.TaskStatus;

namespace Mindflow.Api.Models.Dtos;

public record IntegrationSettingsResponse(
    bool Enabled,
    IReadOnlyList<IntegrationTokenResponse> Tokens);

public record UpdateIntegrationSettingsRequest(bool Enabled);

public record CreateIntegrationTokenRequest(
    string Name,
    IReadOnlyCollection<IntegrationTokenScope> Scopes,
    DateTimeOffset ExpiresAt);

public record IntegrationTokenResponse(
    Guid Id,
    string Name,
    string TokenPrefix,
    IReadOnlyCollection<IntegrationTokenScope> Scopes,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? LastUsedAt,
    bool IsRevoked,
    DateTimeOffset? RevokedAt);

public record CreateIntegrationTokenResponse(
    Guid Id,
    string Name,
    string TokenPrefix,
    IReadOnlyCollection<IntegrationTokenScope> Scopes,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string Token);

public record IntegrationProjectResponse(
    Guid Id,
    string Name,
    string Color,
    Guid? SpaceId,
    DateTimeOffset CreatedAt);

public record IntegrationPageQuery(int Limit, int Offset);

public record IntegrationProjectPageResponse(
    IReadOnlyList<IntegrationProjectResponse> Items,
    int Total,
    int Limit,
    int Offset);

public record IntegrationTimeEntryPageResponse(
    IReadOnlyList<TaskTimeEntryResponse> Items,
    int Total,
    int Limit,
    int Offset);

public record IntegrationTaskQuery(
    Guid? ProjectId,
    TaskStatus? Status,
    bool? IsCompleted,
    DateOnly? DueBefore,
    DateTimeOffset? CreatedAfter,
    int Limit,
    int Offset);

public record IntegrationTaskPageResponse(
    IReadOnlyList<TaskListResponse> Items,
    int Total,
    int Limit,
    int Offset);

public record IntegrationScopeDoc(
    string Scope,
    string Permission,
    string Allows);

public record IntegrationEndpointDoc(
    string Method,
    string Path,
    string Scope,
    string Description);

public record IntegrationAuthDoc(
    string Type,
    string AuthorizationHeader);

public record IntegrationDocsResponse(
    string Name,
    IntegrationAuthDoc Authentication,
    IReadOnlyList<IntegrationScopeDoc> Scopes,
    IReadOnlyList<IntegrationEndpointDoc> Endpoints);
