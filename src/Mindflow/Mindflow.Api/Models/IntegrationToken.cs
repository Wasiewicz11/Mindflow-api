using System.ComponentModel.DataAnnotations;

namespace Mindflow.Api.Models;

public class IntegrationToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    [MaxLength(100)]
    public required string Name { get; set; }
    /// <summary>Hex-encoded HMAC-SHA256 of the plaintext token: 64 characters.</summary>
    [MaxLength(64)]
    public required string TokenHash { get; set; }
    [MaxLength(20)]
    public required string TokenPrefix { get; set; }
    public ICollection<IntegrationTokenPermission> Permissions { get; set; } = [];
    /// <summary>Set for tokens issued to a registered app through the authorization code flow.</summary>
    [MaxLength(40)]
    public string? ClientId { get; set; }
    public Guid? DefaultProjectId { get; set; }
    /// <summary>When set, the token sees and changes only the data of this space.</summary>
    public Guid? SpaceId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public bool IsRevoked { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
