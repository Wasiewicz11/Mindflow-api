using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Mindflow.Api.Services.Ai;

public class GeminiSuggestionProvider(
    HttpClient http,
    IOptions<AiOptions> options,
    ILogger<GeminiSuggestionProvider> logger) : IAiSuggestionProvider
{
    private readonly AiOptions _options = options.Value;

    public string Name => "Gemini";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.Gemini.ApiKey);

    public async Task<IReadOnlyList<SuggestionDraft>> GenerateAsync(DaySnapshot snapshot, CancellationToken ct = default)
    {
        var model = _options.Gemini.Model;
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={_options.Gemini.ApiKey}";

        var body = new
        {
            system_instruction = new { parts = new[] { new { text = SuggestionPromptBuilder.SystemInstruction(_options.MaxSuggestionsPerRun) } } },
            contents = new[] { new { role = "user", parts = new[] { new { text = SuggestionPromptBuilder.BuildUserPayload(snapshot) } } } },
            generationConfig = new { responseMimeType = "application/json", temperature = 0.4 }
        };

        using var response = await http.PostAsJsonAsync(url, body, ct);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var text = doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();

        var drafts = SuggestionPromptBuilder.Parse(text, snapshot);
        logger.LogDebug("Gemini zwrócił {Count} sugestii.", drafts.Count);
        return drafts;
    }
}
