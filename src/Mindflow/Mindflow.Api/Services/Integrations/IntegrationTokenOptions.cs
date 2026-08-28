namespace Mindflow.Api.Services.Integrations;

public class IntegrationTokenOptions
{
    public const string SectionName = "IntegrationTokens";

    /// <summary>Secret mixed into the HMAC of every integration token. At least 32 bytes, stable across deploys.</summary>
    public string? HashPepper { get; set; }

    public int MaxActiveTokensPerUser { get; set; } = 10;

    public int MaxLifetimeDays { get; set; } = 365;

    /// <summary>How stale LastUsedAt may get before it is written again.</summary>
    public int LastUsedThrottleMinutes { get; set; } = 5;

    public int RateLimitPermitsPerMinute { get; set; } = 120;

    /// <summary>Without a pepper the integration API stays off; the rest of the API keeps running.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(HashPepper);
}
