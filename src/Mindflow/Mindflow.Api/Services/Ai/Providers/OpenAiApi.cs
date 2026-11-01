using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Mindflow.Api.Services.Ai.Providers;

public class OpenAiApi(HttpClient http)
{
    private const string BaseUrl = "https://api.openai.com/v1/";

    public async Task<string> ChatAsync(string apiKey, string model, object body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "chat/completions")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await http.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw AiProviderErrors.FromStatus(response.StatusCode, text, model);

        using var doc = JsonDocument.Parse(text);
        var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        return string.IsNullOrWhiteSpace(content) ? throw AiProviderErrors.EmptyResponse() : content;
    }

    public async Task<string> TranscribeAsync(string apiKey, string model, byte[] audio, string mimeType, CancellationToken ct)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(audio);
        file.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
        form.Add(file, "file", "dictation." + AudioFormats.Extension(mimeType));
        form.Add(new StringContent(model), "model");
        form.Add(new StringContent("pl"), "language");
        form.Add(new StringContent("json"), "response_format");

        using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "audio/transcriptions") { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await http.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw AiProviderErrors.FromStatus(response.StatusCode, text, model);

        using var doc = JsonDocument.Parse(text);
        var transcript = doc.RootElement.GetProperty("text").GetString();
        return string.IsNullOrWhiteSpace(transcript) ? throw AiProviderErrors.EmptyResponse() : transcript.Trim();
    }

    public async Task CheckModelAsync(string apiKey, string model, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + "models/" + Uri.EscapeDataString(model));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw AiProviderErrors.FromStatus(response.StatusCode, await response.Content.ReadAsStringAsync(ct), model);
    }
}
