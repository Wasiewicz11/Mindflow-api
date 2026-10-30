using System.ComponentModel.DataAnnotations;

namespace Mindflow.Api.Models;

public class IntegrationAuthorizationCode
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    [MaxLength(40)]
    public required string ClientId { get; set; }
    /// <summary>Hex-encoded SHA-256 of the plaintext code.</summary>
    [MaxLength(64)]
    public required string CodeHash { get; set; }
    [MaxLength(2048)]
    public required string RedirectUri { get; set; }
    public List<string> Scopes { get; set; } = [];
    [MaxLength(43)]
    public required string CodeChallenge { get; set; }
    public Guid? DefaultProjectId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
}
