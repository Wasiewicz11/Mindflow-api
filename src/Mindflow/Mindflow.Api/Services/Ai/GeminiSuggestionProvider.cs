using Microsoft.Extensions.Options;
using Mindflow.Api.Exceptions;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Services.Ai.Providers;

namespace Mindflow.Api.Services.Ai;

public class GeminiSuggestionProvider(
    GeminiApi gemini,
    IOptions<AiOptions> options,
    ILogger<GeminiSuggestionProvider> logger) : IAiSuggestionProvider
{
    private readonly AiOptions _options = options.Value;

    public string Name => "Gemini";

    public AiProvider? Provider => AiProvider.Gemini;

    public async Task<IReadOnlyList<SuggestionDraft>> GenerateAsync(DaySnapshot snapshot, AiCredentials? credentials, CancellationToken ct = default)
    {
        if (credentials is null) return [];

        string text;
        try
        {
            text = await gemini.GenerateAsync(credentials.ApiKey, credentials.Model, Body(credentials.Model, snapshot), ct);
        }
        catch (AiProviderException ex) when (ex.Retryable && !string.Equals(credentials.Model, _options.Dictation.GeminiFallbackModel, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Gemini {Model} failed with {Status}; retrying on {Fallback}.", credentials.Model, ex.StatusCode, _options.Dictation.GeminiFallbackModel);
            text = await gemini.GenerateAsync(credentials.ApiKey, _options.Dictation.GeminiFallbackModel, Body(_options.Dictation.GeminiFallbackModel, snapshot), ct);
        }

        var drafts = SuggestionPromptBuilder.Parse(text, snapshot);
        logger.LogDebug("Gemini zwrócił {Count} sugestii.", drafts.Count);
        return drafts;
    }

    private object Body(string model, DaySnapshot snapshot)
    {
        var generationConfig = new Dictionary<string, object>
        {
            ["responseMimeType"] = "application/json",
            ["temperature"] = 0.4
        };
        if (GeminiApi.ThinkingConfig(model) is { } thinking) generationConfig["thinkingConfig"] = thinking;

        return new
        {
            system_instruction = new { parts = new[] { new { text = SuggestionPromptBuilder.SystemInstruction(_options.MaxSuggestionsPerRun) } } },
            contents = new[] { new { role = "user", parts = new[] { new { text = SuggestionPromptBuilder.BuildUserPayload(snapshot) } } } },
            generationConfig
        };
    }
}
