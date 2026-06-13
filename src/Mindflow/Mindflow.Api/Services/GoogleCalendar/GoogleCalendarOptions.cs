namespace Mindflow.Api.Services.GoogleCalendar;

public class GoogleCalendarOptions
{
    public const string SectionName = "Google:Calendar";

    /// <summary>Backend callback Google redirects to after consent. Must be whitelisted in the Google Cloud OAuth client.</summary>
    public string? RedirectUri { get; set; }

    /// <summary>Public HTTPS address of the push webhook (e.g. https://api.example.com/integrations/google/calendar/webhook). Empty disables watch channels (e.g. local dev).</summary>
    public string? WebhookUrl { get; set; }

    /// <summary>Where the user is sent back after the callback finishes. Falls back to Cors:FrontendUrl.</summary>
    public string? FrontendReturnUrl { get; set; }

    /// <summary>Base64 32-byte key used to encrypt OAuth tokens at rest. When empty, tokens are stored as plaintext (dev only).</summary>
    public string? TokenEncryptionKey { get; set; }

    public string DedicatedCalendarName { get; set; } = "Mindflow";

    /// <summary>How far back/forward the initial (full) mirror sync reaches.</summary>
    public int SyncWindowPastDays { get; set; } = 14;
    public int SyncWindowFutureDays { get; set; } = 120;
}
