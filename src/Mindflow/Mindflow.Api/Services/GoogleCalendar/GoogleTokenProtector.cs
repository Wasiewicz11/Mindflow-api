using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Mindflow.Api.Services.GoogleCalendar;

/// <summary>
/// AES-256-GCM at-rest encryption for OAuth tokens, keyed off a stable config secret
/// (so cipher-text survives process restarts on Render). When no key is configured we
/// store plaintext and tag it so dev still works without setup.
/// </summary>
public class GoogleTokenProtector : IGoogleTokenProtector
{
    private const string PlaintextPrefix = "plain:";
    private const string CipherPrefix = "v1:";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[]? _key;
    private readonly ILogger<GoogleTokenProtector> _logger;

    public GoogleTokenProtector(IOptions<GoogleCalendarOptions> options, ILogger<GoogleTokenProtector> logger)
    {
        _logger = logger;
        var configuredKey = options.Value.TokenEncryptionKey;
        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            _key = null;
            return;
        }

        var bytes = Convert.FromBase64String(configuredKey);
        if (bytes.Length != 32)
            throw new InvalidOperationException("Google:Calendar:TokenEncryptionKey must be a base64-encoded 32-byte key.");
        _key = bytes;
    }

    public string Protect(string plaintext)
    {
        if (_key is null)
        {
            _logger.LogWarning("Google:Calendar:TokenEncryptionKey is not configured — storing OAuth token as plaintext (dev only).");
            return PlaintextPrefix + plaintext;
        }

        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipher, tag);

        // layout: nonce | tag | cipher
        var payload = new byte[NonceSize + TagSize + cipher.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, payload, NonceSize, TagSize);
        Buffer.BlockCopy(cipher, 0, payload, NonceSize + TagSize, cipher.Length);

        return CipherPrefix + Convert.ToBase64String(payload);
    }

    public string Unprotect(string protectedValue)
    {
        if (protectedValue.StartsWith(PlaintextPrefix, StringComparison.Ordinal))
            return protectedValue[PlaintextPrefix.Length..];

        if (!protectedValue.StartsWith(CipherPrefix, StringComparison.Ordinal))
            return protectedValue; // legacy / unprefixed — treat as plaintext

        if (_key is null)
            throw new InvalidOperationException("Encrypted Google token found but Google:Calendar:TokenEncryptionKey is not configured.");

        var payload = Convert.FromBase64String(protectedValue[CipherPrefix.Length..]);
        var nonce = payload.AsSpan(0, NonceSize);
        var tag = payload.AsSpan(NonceSize, TagSize);
        var cipher = payload.AsSpan(NonceSize + TagSize);
        var plainBytes = new byte[cipher.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plainBytes);

        return Encoding.UTF8.GetString(plainBytes);
    }
}
