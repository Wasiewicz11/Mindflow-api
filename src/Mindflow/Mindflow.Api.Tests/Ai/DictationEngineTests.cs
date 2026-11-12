using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Mindflow.Api.Exceptions;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Services.Ai;
using Mindflow.Api.Services.Ai.Dictation;
using Mindflow.Api.Services.Ai.Providers;
using Mindflow.Api.Tests.Infrastructure;

namespace Mindflow.Api.Tests.Ai;

public class DictationEngineTests
{
    private const string Answer = """{"candidates":[{"content":{"parts":[{"text":"{\"transcript\":\"t\",\"tasks\":[]}"}]}}]}""";

    private static readonly DictationContext Context = new(new DateOnly(2026, 11, 1), new TimeOnly(12, 0), "UTC", []);
    private static readonly AiCredentials Gemini = new(AiProvider.Gemini, "gemini-key", "gemini-3.5-flash", AiKeySource.Server);
    private static readonly DictationInput Audio = new(null, [1, 2, 3, 4], "audio/webm");

    private static GeminiDictationEngine CreateGemini(FakeAiHandler handler) =>
        new(new GeminiApi(new HttpClient(handler)), Options.Create(new AiOptions()), NullLogger<GeminiDictationEngine>.Instance);

    [Fact]
    public async Task Gemini_gets_the_audio_inline_with_a_json_schema_and_the_key_in_a_header()
    {
        var handler = new FakeAiHandler();
        handler.Respond((_, _) => FakeAiHandler.Json(Answer));

        var result = await CreateGemini(handler).RunAsync(Gemini, Audio, Context, TaskDictationPrompts.Instance, CancellationToken.None);

        var (request, body) = Assert.Single(handler.Requests);
        using var doc = JsonDocument.Parse(body);
        var parts = doc.RootElement.GetProperty("contents")[0].GetProperty("parts");
        var config = doc.RootElement.GetProperty("generationConfig");

        Assert.Equal("{\"transcript\":\"t\",\"tasks\":[]}", result.Json);
        Assert.EndsWith("/models/gemini-3.5-flash:generateContent", request.RequestUri!.AbsolutePath);
        Assert.DoesNotContain("gemini-key", request.RequestUri.ToString());
        Assert.Equal("gemini-key", request.Headers.GetValues("x-goog-api-key").Single());
        Assert.Equal("audio/webm", parts[0].GetProperty("inline_data").GetProperty("mime_type").GetString());
        Assert.Equal("AQIDBA==", parts[0].GetProperty("inline_data").GetProperty("data").GetString());
        Assert.Equal("application/json", config.GetProperty("responseMimeType").GetString());
        Assert.Equal("OBJECT", config.GetProperty("responseSchema").GetProperty("type").GetString());
        Assert.Equal("low", config.GetProperty("thinkingConfig").GetProperty("thinkingLevel").GetString());
    }

    [Fact]
    public async Task Gemini_retries_once_on_the_fallback_model_when_overloaded()
    {
        var handler = new FakeAiHandler();
        handler.Respond((request, _) => request.RequestUri!.AbsolutePath.Contains("gemini-3.5-flash-lite")
            ? FakeAiHandler.Json(Answer)
            : FakeAiHandler.Json("""{"error":{"message":"high demand"}}""", HttpStatusCode.ServiceUnavailable));

        var result = await CreateGemini(handler).RunAsync(Gemini, Audio, Context, TaskDictationPrompts.Instance, CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("gemini-3.5-flash-lite", result.Model);
    }

    [Fact]
    public async Task Gemini_does_not_retry_a_rejected_key()
    {
        var handler = new FakeAiHandler();
        handler.Respond((_, _) => FakeAiHandler.Json("""{"error":{"message":"API key not valid"}}""", HttpStatusCode.BadRequest));

        var error = await Assert.ThrowsAsync<AiProviderException>(() => CreateGemini(handler).RunAsync(Gemini, Audio, Context, TaskDictationPrompts.Instance, CancellationToken.None));

        Assert.Single(handler.Requests);
        Assert.Equal(422, error.StatusCode);
    }

    [Fact]
    public async Task OpenAi_transcribes_first_and_then_asks_for_strict_json()
    {
        var handler = new FakeAiHandler();
        handler.Respond((request, _) => request.RequestUri!.AbsolutePath.EndsWith("/audio/transcriptions")
            ? FakeAiHandler.Json("""{"text":"Jutro kupić mleko."}""")
            : FakeAiHandler.Json("""{"choices":[{"message":{"content":"{\"transcript\":\"\",\"tasks\":[]}"}}]}"""));
        var engine = new OpenAiDictationEngine(new OpenAiApi(new HttpClient(handler)), Options.Create(new AiOptions()));

        var result = await engine.RunAsync(new AiCredentials(AiProvider.OpenAi, "sk-test", "gpt-4o-mini", AiKeySource.User), Audio, Context, TaskDictationPrompts.Instance, CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("Jutro kupić mleko.", result.Transcript);
        Assert.Contains("gpt-4o-mini-transcribe", handler.Requests[0].Body);
        Assert.Contains("filename=dictation.webm", handler.Requests[0].Body);
        using var chat = JsonDocument.Parse(handler.Requests[1].Body);
        Assert.Equal("json_schema", chat.RootElement.GetProperty("response_format").GetProperty("type").GetString());
        Assert.True(chat.RootElement.GetProperty("response_format").GetProperty("json_schema").GetProperty("strict").GetBoolean());
        Assert.Contains("Jutro kupić mleko.", chat.RootElement.GetProperty("messages")[1].GetProperty("content").GetString());
        Assert.Equal("Bearer", handler.Requests[1].Request.Headers.Authorization!.Scheme);
    }

    [Theory]
    [InlineData("audio/webm;codecs=opus", "audio/webm")]
    [InlineData("audio/mp4", "audio/mp4")]
    [InlineData("audio/x-m4a", "audio/mp4")]
    [InlineData("audio/ogg; codecs=opus", "audio/ogg")]
    [InlineData("image/png", null)]
    [InlineData(null, null)]
    public void Normalizes_browser_audio_types(string? contentType, string? expected)
    {
        Assert.Equal(expected, AudioFormats.Normalize(contentType));
    }
}
