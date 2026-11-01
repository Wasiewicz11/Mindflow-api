using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Repositories;

namespace Mindflow.Api.Services.Ai;

public class AiCredentialResolver(
    IUserAiSettingsRepository settingsRepository,
    IAiKeyProtector protector,
    IOptions<AiOptions> options,
    ILogger<AiCredentialResolver> logger) : IAiCredentialResolver
{
    private const string GeminiDefault = "gemini-3.5-flash";
    private const string OpenAiDefault = "gpt-4o-mini";

    private readonly AiOptions _options = options.Value;

    public async Task<IReadOnlyList<AiCredentials>> ResolveAsync(Guid userId, CancellationToken ct = default)
    {
        var settings = await settingsRepository.GetByUserIdAsync(userId, ct);
        if (settings?.ApiKeyEncrypted is { } encrypted && protector.IsConfigured)
        {
            try
            {
                var key = protector.Unprotect(userId, encrypted);
                var model = string.IsNullOrWhiteSpace(settings.Model) ? DefaultModel(settings.Provider) : settings.Model;
                return [new AiCredentials(settings.Provider, key, model, AiKeySource.User)];
            }
            catch (CryptographicException ex)
            {
                logger.LogWarning(ex, "Saved AI key of user {UserId} cannot be decrypted; falling back to the instance key.", userId);
            }
        }

        return ServerCredentials();
    }

    public string DefaultModel(AiProvider provider) => provider switch
    {
        AiProvider.OpenAi => string.IsNullOrWhiteSpace(_options.OpenAi.Model) ? OpenAiDefault : _options.OpenAi.Model,
        _ => string.IsNullOrWhiteSpace(_options.Gemini.Model) ? GeminiDefault : _options.Gemini.Model
    };

    private List<AiCredentials> ServerCredentials()
    {
        var result = new List<AiCredentials>();
        foreach (var name in _options.ProviderOrder)
        {
            if (!Enum.TryParse<AiProvider>(name, ignoreCase: true, out var provider)) continue;
            var key = provider == AiProvider.Gemini ? _options.Gemini.ApiKey : _options.OpenAi.ApiKey;
            if (!string.IsNullOrWhiteSpace(key) && result.All(c => c.Provider != provider))
                result.Add(new AiCredentials(provider, key, DefaultModel(provider), AiKeySource.Server));
        }
        return result;
    }
}
