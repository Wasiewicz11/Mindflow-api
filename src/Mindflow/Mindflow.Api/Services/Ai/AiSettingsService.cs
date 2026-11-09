using Microsoft.Extensions.Options;
using Mindflow.Api.Exceptions;
using Mindflow.Api.Models;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Repositories;
using Mindflow.Api.Services.Ai.Providers;

namespace Mindflow.Api.Services.Ai;

public class AiSettingsService(
    IUserAiSettingsRepository repository,
    IAiUsageRepository usageRepository,
    IAiCredentialResolver resolver,
    IAiKeyProtector protector,
    IAiConnectionChecker checker,
    ICurrentUserService currentUser,
    IOptions<AiOptions> options) : IAiSettingsService
{
    private readonly AiOptions _options = options.Value;

    public async Task<AiSettingsResponse> GetAsync(CancellationToken ct = default)
    {
        var userId = await currentUser.GetCurrentUserIdAsync();
        return await BuildResponseAsync(userId, await repository.GetByUserIdAsync(userId, ct), ct);
    }

    public async Task<AiSettingsResponse> SaveAsync(UpdateAiSettingsRequest request, CancellationToken ct = default)
    {
        var userId = await currentUser.GetCurrentUserIdAsync();
        var apiKey = NormalizeKey(request.ApiKey);
        var model = NormalizeModel(request.Model);
        if (apiKey is not null && !protector.IsConfigured)
            throw new ServiceUnavailableException("Serwer nie ma skonfigurowanego szyfrowania kluczy AI (Ai:KeyEncryptionKey).");

        var now = DateTimeOffset.UtcNow;
        var settings = await repository.GetByUserIdAsync(userId, ct)
            ?? new UserAiSettings { Id = Guid.NewGuid(), UserId = userId, Provider = request.Provider, CreatedAt = now };

        if (settings.Provider != request.Provider)
        {
            settings.ApiKeyEncrypted = null;
            settings.ApiKeyLast4 = null;
        }

        settings.Provider = request.Provider;
        settings.Model = model;
        if (apiKey is not null)
        {
            settings.ApiKeyEncrypted = protector.Protect(userId, apiKey);
            settings.ApiKeyLast4 = apiKey[^4..];
        }
        settings.UpdatedAt = now;

        await repository.SaveAsync(settings, ct);
        return await BuildResponseAsync(userId, settings, ct);
    }

    public async Task<AiSettingsResponse> DeleteKeyAsync(CancellationToken ct = default)
    {
        var userId = await currentUser.GetCurrentUserIdAsync();
        var settings = await repository.GetByUserIdAsync(userId, ct);
        if (settings is not null && settings.ApiKeyEncrypted is not null)
        {
            settings.ApiKeyEncrypted = null;
            settings.ApiKeyLast4 = null;
            settings.UpdatedAt = DateTimeOffset.UtcNow;
            await repository.SaveAsync(settings, ct);
        }
        return await BuildResponseAsync(userId, settings, ct);
    }

    public async Task<AiConnectionTestResponse> TestAsync(TestAiConnectionRequest request, CancellationToken ct = default)
    {
        var userId = await currentUser.GetCurrentUserIdAsync();
        AiCredentials? credentials;
        var apiKey = NormalizeKey(request.ApiKey);
        if (apiKey is not null)
        {
            var provider = request.Provider ?? AiProvider.Gemini;
            credentials = new AiCredentials(provider, apiKey, NormalizeModel(request.Model) ?? resolver.DefaultModel(provider), AiKeySource.User);
        }
        else
        {
            credentials = (await resolver.ResolveAsync(userId, ct)).FirstOrDefault();
            var model = NormalizeModel(request.Model);
            if (credentials is not null && model is not null && credentials.Source == AiKeySource.User)
                credentials = credentials with { Model = model };
        }

        if (credentials is null)
            return new AiConnectionTestResponse(false, null, null, "none", "Brak klucza API. Wklej klucz, żeby korzystać z AI.");

        var source = SourceName(credentials.Source);
        try
        {
            await checker.CheckAsync(credentials, ct);
            return new AiConnectionTestResponse(true, credentials.Provider, credentials.Model, source, "Połączenie działa.");
        }
        catch (AiProviderException ex)
        {
            return new AiConnectionTestResponse(false, credentials.Provider, credentials.Model, source, ex.Message);
        }
    }

    private async Task<AiSettingsResponse> BuildResponseAsync(Guid userId, UserAiSettings? settings, CancellationToken ct)
    {
        var credentials = await resolver.ResolveAsync(userId, ct);
        var active = credentials.FirstOrDefault();
        var serverProvider = ServerProvider();
        var provider = settings?.Provider ?? AiProvider.Gemini;
        var ownKey = active?.Source == AiKeySource.User;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var dictationUsed = await usageRepository.GetDictationCallsAsync(userId, today, ct);
        var suggestionsUsed = await usageRepository.GetAiCallsAsync(userId, today);

        return new AiSettingsResponse(
            provider,
            string.IsNullOrWhiteSpace(settings?.Model) ? resolver.DefaultModel(provider) : settings.Model,
            string.IsNullOrWhiteSpace(settings?.Model),
            settings?.ApiKeyEncrypted is not null,
            settings?.ApiKeyLast4,
            protector.IsConfigured,
            serverProvider is not null,
            serverProvider,
            active is null ? "none" : SourceName(active.Source),
            new Dictionary<string, string>
            {
                [nameof(AiProvider.Gemini)] = resolver.DefaultModel(AiProvider.Gemini),
                [nameof(AiProvider.OpenAi)] = resolver.DefaultModel(AiProvider.OpenAi)
            },
            new AiUsageResponse(dictationUsed, ownKey ? _options.Dictation.OwnKeyDailyLimit : _options.Dictation.DailyLimit),
            new AiUsageResponse(suggestionsUsed, ownKey ? _options.OwnKeyManualAiDailyLimit : _options.ManualAiDailyLimit));
    }

    private AiProvider? ServerProvider()
    {
        foreach (var name in _options.ProviderOrder)
        {
            if (!Enum.TryParse<AiProvider>(name, ignoreCase: true, out var provider)) continue;
            var key = provider == AiProvider.Gemini ? _options.Gemini.ApiKey : _options.OpenAi.ApiKey;
            if (!string.IsNullOrWhiteSpace(key)) return provider;
        }
        return null;
    }

    public static string SourceName(AiKeySource source) => source == AiKeySource.User ? "user" : "server";

    private static string? NormalizeKey(string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return null;
        var key = apiKey.Trim();
        if (key.Length is < 10 or > 300 || key.Any(char.IsWhiteSpace))
            throw new BadRequestException("Klucz API ma nieprawidłowy format.");
        return key;
    }

    private static string? NormalizeModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model)) return null;
        var value = model.Trim();
        return AiModelName.IsValid(value) ? value : throw new BadRequestException("Nazwa modelu ma nieprawidłowy format.");
    }
}
