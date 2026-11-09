using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Mindflow.Api.Data;
using Mindflow.Api.Exceptions;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Repositories;
using Mindflow.Api.Services.Ai.Providers;

namespace Mindflow.Api.Services.Ai.Dictation;

public class DictationService(
    IAiCredentialResolver resolver,
    IEnumerable<IDictationEngine> engines,
    IAiUsageRepository usageRepository,
    IProjectRepository projectRepository,
    MindflowDbContext db,
    ICurrentUserService currentUser,
    IOptions<AiOptions> options,
    ILogger<DictationService> logger) : IDictationService
{
    private readonly DictationOptions _options = options.Value.Dictation;

    public async Task<DictationResponse> ProcessAsync(DictationInput input, CancellationToken ct = default)
    {
        var userId = await currentUser.GetCurrentUserIdAsync();
        Validate(input);

        var credentials = (await resolver.ResolveAsync(userId, ct)).FirstOrDefault()
            ?? throw new ServiceUnavailableException("AI nie jest skonfigurowane. Dodaj klucz w Ustawieniach → AI.");

        var limit = credentials.Source == AiKeySource.User ? _options.OwnKeyDailyLimit : _options.DailyLimit;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var used = await usageRepository.GetDictationCallsAsync(userId, today, ct);
        if (used >= limit)
            throw new TooManyRequestsException($"Wykorzystano dzienny limit dyktowania ({limit}). Spróbuj jutro albo dodaj własny klucz w Ustawieniach → AI.");
        await usageRepository.IncrementDictationCallsAsync(userId, today, ct);

        var context = await BuildContextAsync(userId, ct);
        var engine = engines.FirstOrDefault(e => e.Provider == credentials.Provider)
            ?? throw new ServiceUnavailableException("Ten dostawca AI nie obsługuje dyktowania.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        DictationEngineResult result;
        try
        {
            result = await engine.RunAsync(credentials, input, context, timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw AiProviderErrors.Timeout();
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Dictation call to {Provider} failed.", credentials.Provider);
            throw new AiProviderException("Nie udało się połączyć z dostawcą AI.", StatusCodes.Status502BadGateway);
        }

        var parsed = DictationResultParser.Parse(result.Json, context, _options.MaxTasks);
        var transcript = input.IsAudio ? result.Transcript ?? parsed.Transcript : input.Text!.Trim();
        logger.LogInformation("Dictation via {Provider}/{Model} ({Source}) produced {Count} tasks.",
            credentials.Provider, result.Model, credentials.Source, parsed.Tasks.Count);

        return new DictationResponse(
            transcript,
            parsed.Tasks,
            credentials.Provider,
            result.Model,
            AiSettingsService.SourceName(credentials.Source),
            new AiUsageResponse(used + 1, limit));
    }

    private void Validate(DictationInput input)
    {
        if (input.IsAudio)
        {
            if (input.Audio!.Length > _options.MaxAudioBytes)
                throw new AiProviderException("Nagranie jest za długie.", StatusCodes.Status413PayloadTooLarge);
            if (input.AudioMimeType is null)
                throw new AiProviderException("Nieobsługiwany format nagrania.", StatusCodes.Status415UnsupportedMediaType);
            return;
        }

        var text = input.Text?.Trim();
        if (string.IsNullOrEmpty(text))
            throw new BadRequestException("Nagraj albo wpisz notatkę.");
        if (text.Length > _options.MaxTextLength)
            throw new BadRequestException($"Notatka może mieć najwyżej {_options.MaxTextLength} znaków.");
    }

    private async Task<DictationContext> BuildContextAsync(Guid userId, CancellationToken ct)
    {
        var timeZoneId = await db.Users.Where(u => u.Id == userId).Select(u => u.TimeZone).FirstOrDefaultAsync(ct);
        var zone = ResolveZone(timeZoneId);
        var local = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone);
        var projects = await projectRepository.GetAccessibleForUserAsync(userId, ct);

        return new DictationContext(
            DateOnly.FromDateTime(local.DateTime),
            TimeOnly.FromDateTime(local.DateTime),
            zone.Id,
            projects.Select(p => new DictationProject(p.Id, p.Name)).ToList());
    }

    private static TimeZoneInfo ResolveZone(string? id) =>
        !string.IsNullOrWhiteSpace(id) && TimeZoneInfo.TryFindSystemTimeZoneById(id.Trim(), out var zone) ? zone : TimeZoneInfo.Utc;
}
