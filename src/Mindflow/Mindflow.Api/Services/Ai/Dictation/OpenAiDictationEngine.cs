using Microsoft.Extensions.Options;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Services.Ai.Providers;

namespace Mindflow.Api.Services.Ai.Dictation;

/// <summary>Two steps: speech-to-text, then a chat completion constrained by a JSON schema.</summary>
public class OpenAiDictationEngine(OpenAiApi openAi, IOptions<AiOptions> options) : IDictationEngine
{
    private readonly DictationOptions _options = options.Value.Dictation;

    public AiProvider Provider => AiProvider.OpenAi;

    public async Task<DictationEngineResult> RunAsync(AiCredentials credentials, DictationInput input, DictationContext context, CancellationToken ct)
    {
        var transcript = input.IsAudio
            ? await openAi.TranscribeAsync(credentials.ApiKey, _options.OpenAiTranscriptionModel, input.Audio!, input.AudioMimeType!, ct)
            : null;

        var json = await openAi.ChatAsync(credentials.ApiKey, credentials.Model, BuildRequest(credentials.Model, transcript ?? input.Text ?? "", context), ct);
        return new DictationEngineResult(json, transcript, credentials.Model);
    }

    public static object BuildRequest(string model, string text, DictationContext context) => new
    {
        model,
        temperature = 0.2,
        messages = new[]
        {
            new { role = "system", content = DictationPrompt.SystemInstruction(context, audio: false) },
            new { role = "user", content = DictationPrompt.UserText(text) }
        },
        response_format = new
        {
            type = "json_schema",
            json_schema = new { name = "dictated_tasks", strict = true, schema = DictationPrompt.OpenAiSchema(context) }
        }
    };
}
