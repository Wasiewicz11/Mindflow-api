using Microsoft.Extensions.Options;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Services.Ai.Providers;

namespace Mindflow.Api.Services.Ai.Dictation;

/// <summary>Two steps: speech-to-text, then a chat completion constrained by a JSON schema.</summary>
public class OpenAiDictationEngine(OpenAiApi openAi, IOptions<AiOptions> options) : IDictationEngine
{
    private readonly DictationOptions _options = options.Value.Dictation;

    public AiProvider Provider => AiProvider.OpenAi;

    public async Task<DictationEngineResult> RunAsync(AiCredentials credentials, DictationInput input, DictationContext context, IDictationPrompts prompts, CancellationToken ct)
    {
        var transcript = input.IsAudio
            ? await openAi.TranscribeAsync(credentials.ApiKey, _options.OpenAiTranscriptionModel, input.Audio!, input.AudioMimeType!, ct)
            : null;

        var json = await openAi.ChatAsync(credentials.ApiKey, credentials.Model, BuildRequest(credentials.Model, transcript ?? input.Text ?? "", context, prompts), ct);
        return new DictationEngineResult(json, transcript, credentials.Model);
    }

    public static object BuildRequest(string model, string text, DictationContext context, IDictationPrompts prompts) => new
    {
        model,
        temperature = 0.2,
        messages = new[]
        {
            new { role = "system", content = prompts.SystemInstruction(context, audio: false) },
            new { role = "user", content = prompts.UserText(text) }
        },
        response_format = new
        {
            type = "json_schema",
            json_schema = new { name = prompts.SchemaName, strict = true, schema = prompts.OpenAiSchema(context) }
        }
    };
}
