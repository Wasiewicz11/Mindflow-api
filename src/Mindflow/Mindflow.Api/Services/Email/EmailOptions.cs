namespace Mindflow.Api.Services.Email;

public record EmailOptions(string Host, int Port, string? User, string? Password, string From, bool UseSsl)
{
    public static bool IsConfigured(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration["Email:Smtp:Host"])
        && !string.IsNullOrWhiteSpace(configuration["Email:Smtp:From"]);

    public static EmailOptions? FromConfiguration(IConfiguration configuration) => IsConfigured(configuration)
        ? new EmailOptions(
            configuration["Email:Smtp:Host"]!,
            int.TryParse(configuration["Email:Smtp:Port"], out var port) ? port : 587,
            configuration["Email:Smtp:User"],
            configuration["Email:Smtp:Password"],
            configuration["Email:Smtp:From"]!,
            !bool.TryParse(configuration["Email:Smtp:UseSsl"], out var useSsl) || useSsl)
        : null;
}
