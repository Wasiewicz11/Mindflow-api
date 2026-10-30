using System.ComponentModel.DataAnnotations;
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
    DateTimeOffset? RevokedAt,
    string? ClientId,
    string? ClientName,
    Guid? DefaultProjectId);

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

public record UpsertExternalTaskRequest(
    [MaxLength(1000)] string? Content,
    [MaxLength(10000)] string? Description,
    TaskPriority? Priority,
    TaskStatus? Status,
    DateOnly? DueDate,
    TimeOnly? DueTime,
    [Range(0.01, 1000)] decimal? EstimatedHours,
    Guid? ProjectId,
    IReadOnlyCollection<string>? Tags,
    [MaxLength(2048)] string? ExternalUrl);

public record IntegrationAuthorizationPreviewResponse(
    string ClientId,
    string Name,
    IReadOnlyCollection<IntegrationTokenScope> Scopes,
    string RedirectUri);

public record IntegrationAuthorizationRequest(
    string ClientId,
    string RedirectUri,
    IReadOnlyCollection<IntegrationTokenScope> Scopes,
    [MaxLength(1024)] string? State,
    string CodeChallenge,
    string CodeChallengeMethod,
    Guid? DefaultProjectId);

public record IntegrationAuthorizationResponse(string RedirectUrl);

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
