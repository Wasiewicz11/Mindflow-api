using Mindflow.Api.Models.Enums;

namespace Mindflow.Api.Services.Ai;

public interface IAiCredentialResolver
{
    /// <summary>The user's own key when saved, otherwise the instance keys in <c>Ai:ProviderOrder</c>.</summary>
    Task<IReadOnlyList<AiCredentials>> ResolveAsync(Guid userId, CancellationToken ct = default);

    string DefaultModel(AiProvider provider);
}
