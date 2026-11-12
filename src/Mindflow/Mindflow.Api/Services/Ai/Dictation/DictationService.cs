using Microsoft.Extensions.Options;
using Mindflow.Api.Exceptions;
using Mindflow.Api.Models.Dtos;
using Mindflow.Api.Services.Ai.Providers;

namespace Mindflow.Api.Services.Ai.Dictation;

public class DictationService(
    IAiCredentialResolver resolver,
    IEnumerable<IDictationEngine> engines,
    IDictationQuota quota,
    IDictationContextBuilder contextBuilder,
    ICurrentUserService currentUser,
    IOptions<AiOptions> options,
    ILogger<DictationService> logger) : IDictationService
{
    private readonly DictationOptions _options = options.Value.Dictation;

    public async Task<DictationResponse> ProcessAsync(DictationInput input, CancellationToken ct = default)
    {
        var userId = await currentUser.GetCurrentUserIdAsync();
        DictationInputRules.Validate(input, _options);

        var resolved = (await resolver.ResolveAsync(userId, ct)).FirstOrDefault()
            ?? throw new ServiceUnavailableException("AI nie jest skonfigurowane. Dodaj klucz w Ustawieniach → AI.");

        var reservation = await quota.ReserveAsync(userId, resolved, ct);
        var credentials = reservation.Credentials;

        var context = await contextBuilder.BuildAsync(userId, ct);
        var engine = engines.FirstOrDefault(e => e.Provider == credentials.Provider)
            ?? throw new ServiceUnavailableException("Ten dostawca AI nie obsługuje dyktowania.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        DictationEngineResult result;
        try
        {
            result = await engine.RunAsync(credentials, input, context, TaskDictationPrompts.Instance, timeout.Token);
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
        logger.LogInformation("Dictation via {Provider}/{Model} ({Source}, lite: {Lite}) produced {Count} tasks.",
            credentials.Provider, result.Model, credentials.Source, reservation.Lite, parsed.Tasks.Count);

        return new DictationResponse(
            transcript,
            parsed.Tasks,
            credentials.Provider,
            result.Model,
            AiSettingsService.SourceName(credentials.Source),
            reservation.Usage,
            reservation.Lite);
    }
}
