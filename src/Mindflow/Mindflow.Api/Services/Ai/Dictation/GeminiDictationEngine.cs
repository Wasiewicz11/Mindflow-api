using Microsoft.Extensions.Options;
using Mindflow.Api.Exceptions;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Services.Ai.Providers;

namespace Mindflow.Api.Services.Ai.Dictation;

/// <summary>One call: Gemini hears the audio inline and returns the transcript together with the tasks.</summary>
public class GeminiDictationEngine(GeminiApi gemini, IOptions<AiOptions> options, ILogger<GeminiDictationEngine> logger) : IDictationEngine
{
    private readonly DictationOptions _options = options.Value.Dictation;

    public AiProvider Provider => AiProvider.Gemini;

    public async Task<DictationEngineResult> RunAsync(AiCredentials credentials, DictationInput input, DictationContext context, CancellationToken ct)
    {
        try
        {
            return await CallAsync(credentials.ApiKey, credentials.Model, input, context, ct);
        }
        catch (AiProviderException ex) when (ex.Retryable && CanFallBack(credentials.Model))
        {
            logger.LogWarning("Gemini model {Model} failed with {Status}; retrying on {Fallback}.", credentials.Model, ex.StatusCode, _options.GeminiFallbackModel);
            return await CallAsync(credentials.ApiKey, _options.GeminiFallbackModel, input, context, ct);
        }
    }

    public static object BuildRequest(string model, DictationInput input, DictationContext context)
    {
        var parts = new List<object>();
        if (input.IsAudio)
        {
            parts.Add(new { inline_data = new { mime_type = input.AudioMimeType, data = Convert.ToBase64String(input.Audio!) } });
            parts.Add(new { text = DictationPrompt.AudioUserText });
        }
        else
        {
            parts.Add(new { text = DictationPrompt.UserText(input.Text ?? "") });
        }

        var generationConfig = new Dictionary<string, object>
        {
            ["responseMimeType"] = "application/json",
            ["responseSchema"] = DictationPrompt.GeminiSchema(context),
            ["temperature"] = 0.2
        };
        if (GeminiApi.ThinkingConfig(model) is { } thinking) generationConfig["thinkingConfig"] = thinking;

        return new
        {
            system_instruction = new { parts = new[] { new { text = DictationPrompt.SystemInstruction(context, input.IsAudio) } } },
            contents = new[] { new { role = "user", parts } },
            generationConfig
        };
    }

    private async Task<DictationEngineResult> CallAsync(string apiKey, string model, DictationInput input, DictationContext context, CancellationToken ct)
    {
        var json = await gemini.GenerateAsync(apiKey, model, BuildRequest(model, input, context), ct);
        return new DictationEngineResult(json, null, model);
    }

    private bool CanFallBack(string model) =>
        !string.IsNullOrWhiteSpace(_options.GeminiFallbackModel)
        && !string.Equals(model, _options.GeminiFallbackModel, StringComparison.OrdinalIgnoreCase);
}
