using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Mindflow.Api.Services.Ai;

/// <summary>AES-256-GCM with the user id as associated data. Layout: "v1:" + base64(nonce | tag | cipher).</summary>
public class AiKeyProtector : IAiKeyProtector
{
    private const string Prefix = "v1:";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[]? _key;

    public AiKeyProtector(IOptions<AiOptions> options)
    {
        var configured = options.Value.KeyEncryptionKey;
        if (string.IsNullOrWhiteSpace(configured)) return;

        var bytes = Convert.FromBase64String(configured);
        if (bytes.Length != 32)
            throw new InvalidOperationException("Ai:KeyEncryptionKey must be a base64-encoded 32-byte key.");
        _key = bytes;
    }

    public bool IsConfigured => _key is not null;

    public string Protect(Guid userId, string apiKey)
    {
        var key = RequireKey();
        var plain = Encoding.UTF8.GetBytes(apiKey);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];

        using (var aes = new AesGcm(key, TagSize))
            aes.Encrypt(nonce, plain, cipher, tag, userId.ToByteArray());

        var payload = new byte[NonceSize + TagSize + cipher.Length];
        nonce.CopyTo(payload, 0);
        tag.CopyTo(payload, NonceSize);
        cipher.CopyTo(payload, NonceSize + TagSize);
        return Prefix + Convert.ToBase64String(payload);
    }

    public string Unprotect(Guid userId, string protectedValue)
    {
        var key = RequireKey();
        if (!protectedValue.StartsWith(Prefix, StringComparison.Ordinal))
            throw new CryptographicException("Unknown AI key format.");

        var payload = Convert.FromBase64String(protectedValue[Prefix.Length..]);
        if (payload.Length < NonceSize + TagSize)
            throw new CryptographicException("AI key payload is too short.");

        var cipher = payload.AsSpan(NonceSize + TagSize);
        var plain = new byte[cipher.Length];
        using (var aes = new AesGcm(key, TagSize))
            aes.Decrypt(payload.AsSpan(0, NonceSize), cipher, payload.AsSpan(NonceSize, TagSize), plain, userId.ToByteArray());

        return Encoding.UTF8.GetString(plain);
    }

    private byte[] RequireKey() =>
        _key ?? throw new InvalidOperationException("Ai:KeyEncryptionKey is not configured.");
}
