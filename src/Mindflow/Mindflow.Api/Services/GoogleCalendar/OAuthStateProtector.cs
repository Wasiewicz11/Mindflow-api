using System.Security.Cryptography;
using System.Text;

namespace Mindflow.Api.Services.GoogleCalendar;

public class OAuthStateProtector(IConfiguration configuration) : IOAuthStateProtector
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private byte[] Key =>
        Encoding.UTF8.GetBytes(configuration["Jwt:Secret"]
            ?? throw new InvalidOperationException("Jwt:Secret is not configured."));

    public string Create(Guid userId)
    {
        var expiry = DateTimeOffset.UtcNow.Add(Lifetime).ToUnixTimeSeconds();
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
        var payload = $"{userId:N}|{expiry}|{nonce}";
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var signature = HMACSHA256.HashData(Key, payloadBytes);
        return $"{Base64Url(payloadBytes)}.{Base64Url(signature)}";
    }

    public bool TryRead(string state, out Guid userId)
    {
        userId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(state)) return false;

        var parts = state.Split('.');
        if (parts.Length != 2) return false;

        byte[] payloadBytes;
        byte[] signature;
        try
        {
            payloadBytes = FromBase64Url(parts[0]);
            signature = FromBase64Url(parts[1]);
        }
        catch (FormatException)
        {
            return false;
        }

        var expected = HMACSHA256.HashData(Key, payloadBytes);
        if (!CryptographicOperations.FixedTimeEquals(expected, signature))
            return false;

        var payload = Encoding.UTF8.GetString(payloadBytes).Split('|');
        if (payload.Length != 3) return false;

        if (!Guid.TryParseExact(payload[0], "N", out var parsedUser)) return false;
        if (!long.TryParse(payload[1], out var expiry)) return false;
        if (DateTimeOffset.FromUnixTimeSeconds(expiry) < DateTimeOffset.UtcNow) return false;

        userId = parsedUser;
        return true;
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = (padded.Length % 4) switch
        {
            2 => padded + "==",
            3 => padded + "=",
            _ => padded
        };
        return Convert.FromBase64String(padded);
    }
}
