using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Mindflow.Api.Services.Ai;

public class OpenAiSuggestionProvider(
    HttpClient http,
    IOptions<AiOptions> options,
    ILogger<OpenAiSuggestionProvider> logger) : IAiSuggestionProvider
{
    private readonly AiOptions _options = options.Value;

    public string Name => "OpenAi";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.OpenAi.ApiKey);

    public async Task<IReadOnlyList<SuggestionDraft>> GenerateAsync(DaySnapshot snapshot, CancellationToken ct = default)
    {
        var body = new
        {
            model = _options.OpenAi.Model,
            messages = new[]
            {
                new { role = "system", content = SuggestionPromptBuilder.SystemInstruction(_options.MaxSuggestionsPerRun) },
                new { role = "user", content = SuggestionPromptBuilder.BuildUserPayload(snapshot) }
            },
            response_format = new { type = "json_object" },
            temperature = 0.4
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.OpenAi.ApiKey);

        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var text = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        var drafts = SuggestionPromptBuilder.Parse(text, snapshot);
        logger.LogDebug("OpenAi zwrócił {Count} sugestii.", drafts.Count);
        return drafts;
    }
}
