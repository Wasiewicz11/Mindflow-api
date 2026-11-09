using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace Mindflow.Api.Services.Reminders;

public record ReminderActionClaims(Guid TaskId, Guid UserId, DateTimeOffset ExpiresAt);

public interface IReminderActionTokens
{
    string Create(Guid taskId, Guid userId, DateTimeOffset expiresAt);
    ReminderActionClaims? Validate(string? token, DateTimeOffset now);
}

public class ReminderActionTokens(IConfiguration configuration) : IReminderActionTokens
{
    private readonly Lazy<byte[]> key = new(() =>
    {
        var secret = configuration["Jwt:Secret"];
        if (string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException("Jwt:Secret is not configured.");
        return SHA256.HashData(Encoding.UTF8.GetBytes($"mindflow-reminder-actions:{secret}"));
    });

    public string Create(Guid taskId, Guid userId, DateTimeOffset expiresAt)
    {
        var payload = Encoding.UTF8.GetBytes($"{taskId:N}.{userId:N}.{expiresAt.ToUnixTimeSeconds()}");
        return $"{WebEncoders.Base64UrlEncode(payload)}.{WebEncoders.Base64UrlEncode(Sign(payload))}";
    }

    public ReminderActionClaims? Validate(string? token, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 512) return null;
        var parts = token.Split('.');
        if (parts.Length != 2) return null;

        byte[] payload, signature;
        try
        {
            payload = WebEncoders.Base64UrlDecode(parts[0]);
            signature = WebEncoders.Base64UrlDecode(parts[1]);
        }
        catch (FormatException)
        {
            return null;
        }

        if (!CryptographicOperations.FixedTimeEquals(signature, Sign(payload))) return null;

        var fields = Encoding.UTF8.GetString(payload).Split('.');
        if (fields.Length != 3
            || !Guid.TryParseExact(fields[0], "N", out var taskId)
            || !Guid.TryParseExact(fields[1], "N", out var userId)
            || !long.TryParse(fields[2], out var expiresUnix))
            return null;

        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(expiresUnix);
        return expiresAt <= now ? null : new ReminderActionClaims(taskId, userId, expiresAt);
    }

    private byte[] Sign(byte[] payload) => HMACSHA256.HashData(key.Value, payload);
}
