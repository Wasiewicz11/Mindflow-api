using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Mindflow.Api.Services.Ai;

namespace Mindflow.Api.Tests.Ai;

public class AiKeyProtectorTests
{
    private static readonly string Key = Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());

    private static AiKeyProtector Create(string? key = null) =>
        new(Options.Create(new AiOptions { KeyEncryptionKey = key ?? Key }));

    [Fact]
    public void Round_trips_the_key_for_its_owner()
    {
        var protector = Create();
        var userId = Guid.NewGuid();

        var stored = protector.Protect(userId, "AIza-secret-1234");

        Assert.StartsWith("v1:", stored);
        Assert.DoesNotContain("secret", stored);
        Assert.Equal("AIza-secret-1234", protector.Unprotect(userId, stored));
    }

    [Fact]
    public void Same_key_encrypts_differently_each_time()
    {
        var protector = Create();
        var userId = Guid.NewGuid();

        Assert.NotEqual(protector.Protect(userId, "abc"), protector.Protect(userId, "abc"));
    }

    [Fact]
    public void Cipher_text_copied_to_another_user_does_not_decrypt()
    {
        var protector = Create();
        var stored = protector.Protect(Guid.NewGuid(), "AIza-secret");

        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(Guid.NewGuid(), stored));
    }

    [Fact]
    public void Tampered_cipher_text_is_rejected()
    {
        var protector = Create();
        var userId = Guid.NewGuid();
        var payload = Convert.FromBase64String(protector.Protect(userId, "AIza-secret")[3..]);
        payload[^1] ^= 0xFF;

        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(userId, "v1:" + Convert.ToBase64String(payload)));
    }

    [Fact]
    public void Without_a_configured_key_nothing_is_stored_in_plain_text()
    {
        var protector = new AiKeyProtector(Options.Create(new AiOptions()));

        Assert.False(protector.IsConfigured);
        Assert.Throws<InvalidOperationException>(() => protector.Protect(Guid.NewGuid(), "AIza"));
    }

    [Fact]
    public void Rejects_a_key_of_the_wrong_length()
    {
        Assert.Throws<InvalidOperationException>(() => Create(Convert.ToBase64String(new byte[16])));
    }
}
