using Microsoft.Extensions.Options;

namespace Mindflow.Api.Services.Ai;

public class AiSuggestionOrchestrator(
    IEnumerable<IAiSuggestionProvider> providers,
    IOptions<AiOptions> options,
    ILogger<AiSuggestionOrchestrator> logger) : IAiSuggestionOrchestrator
{
    private readonly AiOptions _options = options.Value;

    public async Task<OrchestratorResult> GenerateAsync(DaySnapshot snapshot, bool aiAllowed, CancellationToken ct = default)
    {
        var ordered = OrderByConfiguredPriority(providers)
            .Where(p => p.IsConfigured)
            .Where(p => aiAllowed || !p.IsAi)
            .ToList();

        if (ordered.Count == 0)
        {
            logger.LogWarning("Brak dostępnego providera (aiAllowed={AiAllowed}) — żadna sugestia nie powstanie.", aiAllowed);
            return new OrchestratorResult(null, false, []);
        }

        foreach (var provider in ordered)
        {
            try
            {
                var drafts = await provider.GenerateAsync(snapshot, ct);
                if (drafts.Count > 0)
                {
                    logger.LogInformation("Sugestie wygenerowane przez providera {Provider}.", provider.Name);
                    return new OrchestratorResult(provider.Name, provider.IsAi, drafts);
                }

                logger.LogInformation("Provider {Provider} nie zwrócił sugestii — próbuję kolejnego.", provider.Name);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Provider {Provider} zawiódł — fallback do kolejnego.", provider.Name);
            }
        }

        return new OrchestratorResult(null, false, []);
    }

    private IEnumerable<IAiSuggestionProvider> OrderByConfiguredPriority(IEnumerable<IAiSuggestionProvider> source)
    {
        var order = _options.ProviderOrder;
        return source.OrderBy(p =>
        {
            var idx = Array.IndexOf(order, p.Name);
            return idx < 0 ? int.MaxValue : idx;
        });
    }
}
