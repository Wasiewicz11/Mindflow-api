namespace Mindflow.Api.Services.Ai;

/// <summary>Encrypts users' AI provider keys; the cipher text is bound to the owning user.</summary>
public interface IAiKeyProtector
{
    bool IsConfigured { get; }
    string Protect(Guid userId, string apiKey);
    string Unprotect(Guid userId, string protectedValue);
}
