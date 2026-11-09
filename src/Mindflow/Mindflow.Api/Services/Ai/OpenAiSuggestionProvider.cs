using Microsoft.Extensions.Options;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Services.Ai.Providers;

namespace Mindflow.Api.Services.Ai;

public class OpenAiSuggestionProvider(
    OpenAiApi openAi,
    IOptions<AiOptions> options,
    ILogger<OpenAiSuggestionProvider> logger) : IAiSuggestionProvider
{
    private readonly AiOptions _options = options.Value;

    public string Name => "OpenAi";

    public AiProvider? Provider => AiProvider.OpenAi;

    public async Task<IReadOnlyList<SuggestionDraft>> GenerateAsync(DaySnapshot snapshot, AiCredentials? credentials, CancellationToken ct = default)
    {
        if (credentials is null) return [];

        var body = new
        {
            model = credentials.Model,
            messages = new[]
            {
                new { role = "system", content = SuggestionPromptBuilder.SystemInstruction(_options.MaxSuggestionsPerRun) },
                new { role = "user", content = SuggestionPromptBuilder.BuildUserPayload(snapshot) }
            },
            response_format = new { type = "json_object" },
            temperature = 0.4
        };

        var text = await openAi.ChatAsync(credentials.ApiKey, credentials.Model, body, ct);
        var drafts = SuggestionPromptBuilder.Parse(text, snapshot);
        logger.LogDebug("OpenAi zwrócił {Count} sugestii.", drafts.Count);
        return drafts;
    }
}
