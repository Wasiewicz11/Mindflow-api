namespace Mindflow.Api.Services.Reminders;

public static class ReminderLinks
{
    public static string FrontendUrl(IConfiguration configuration) =>
        (FirstSet(configuration["Notifications:FrontendUrl"], configuration["Cors:FrontendUrl"]) ?? "").TrimEnd('/');

    public static string TaskUrl(IConfiguration configuration, Guid taskId) =>
        $"{FrontendUrl(configuration)}/zadania/{taskId}";

    public static string ActionApiUrl(IConfiguration configuration) =>
        $"{(configuration["Notifications:PublicApiUrl"] ?? "").TrimEnd('/')}/notifications/reminder-actions";

    public static string ActionPageUrl(IConfiguration configuration, string token) =>
        $"{FrontendUrl(configuration)}/przypomnienie?t={Uri.EscapeDataString(token)}";

    private static string? FirstSet(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
