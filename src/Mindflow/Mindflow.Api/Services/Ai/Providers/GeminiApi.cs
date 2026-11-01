using System.Net.Http.Json;
using System.Text.Json;

namespace Mindflow.Api.Services.Ai.Providers;

/// <summary>Raw calls to the Gemini REST API; the key travels in a header, never in the URL.</summary>
public class GeminiApi(HttpClient http)
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models/";

    public async Task<string> GenerateAsync(string apiKey, string model, object body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}{Uri.EscapeDataString(model)}:generateContent")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("x-goog-api-key", apiKey);

        using var response = await http.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw AiProviderErrors.FromStatus(response.StatusCode, text, model);

        using var doc = JsonDocument.Parse(text);
        if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
            throw AiProviderErrors.EmptyResponse();
        if (!candidates[0].TryGetProperty("content", out var content) || !content.TryGetProperty("parts", out var parts))
            throw AiProviderErrors.EmptyResponse();

        var output = string.Concat(parts.EnumerateArray()
            .Where(p => !(p.TryGetProperty("thought", out var thought) && thought.ValueKind == JsonValueKind.True))
            .Select(p => p.TryGetProperty("text", out var t) ? t.GetString() : null));
        return string.IsNullOrWhiteSpace(output) ? throw AiProviderErrors.EmptyResponse() : output;
    }

    public async Task CheckModelAsync(string apiKey, string model, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}{Uri.EscapeDataString(model)}");
        request.Headers.Add("x-goog-api-key", apiKey);
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw AiProviderErrors.FromStatus(response.StatusCode, await response.Content.ReadAsStringAsync(ct), model);
    }

    public static object? ThinkingConfig(string model) =>
        model.StartsWith("gemini-3", StringComparison.OrdinalIgnoreCase) ? new { thinkingLevel = "low" } : null;
}
