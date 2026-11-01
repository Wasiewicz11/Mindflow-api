using Microsoft.Extensions.Options;

namespace Mindflow.Api.Services.Ai;

public class AiSuggestionOrchestrator(
    IEnumerable<IAiSuggestionProvider> providers,
    IOptions<AiOptions> options,
    ILogger<AiSuggestionOrchestrator> logger) : IAiSuggestionOrchestrator
{
    private readonly AiOptions _options = options.Value;

    public async Task<OrchestratorResult> GenerateAsync(DaySnapshot snapshot, IReadOnlyList<AiCredentials> credentials, bool aiAllowed, CancellationToken ct = default)
    {
        var all = providers.ToList();
        var attempts = new List<(IAiSuggestionProvider Provider, AiCredentials? Credentials)>();

        if (aiAllowed)
        {
            foreach (var creds in credentials)
            {
                var provider = all.FirstOrDefault(p => p.Provider == creds.Provider);
                if (provider is not null) attempts.Add((provider, creds));
            }
        }

        if (_options.EnableLocalFallback)
            attempts.AddRange(all.Where(p => p.Provider is null).Select(p => (p, (AiCredentials?)null)));

        if (attempts.Count == 0)
        {
            logger.LogWarning("Brak dostępnego providera (aiAllowed={AiAllowed}) — żadna sugestia nie powstanie.", aiAllowed);
            return new OrchestratorResult(null, false, []);
        }

        foreach (var (provider, creds) in attempts)
        {
            try
            {
                var drafts = await provider.GenerateAsync(snapshot, creds, ct);
                if (drafts.Count > 0)
                {
                    logger.LogInformation("Sugestie wygenerowane przez providera {Provider} ({Source}).", provider.Name, creds?.Source);
                    return new OrchestratorResult(provider.Name, provider.Provider is not null, drafts);
                }

                logger.LogInformation("Provider {Provider} nie zwrócił sugestii — próbuję kolejnego.", provider.Name);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Provider {Provider} zawiódł — fallback do kolejnego.", provider.Name);
            }
        }

        return new OrchestratorResult(null, false, []);
    }
}
