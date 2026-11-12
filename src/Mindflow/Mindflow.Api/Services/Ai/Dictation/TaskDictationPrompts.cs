namespace Mindflow.Api.Services.Ai.Dictation;

public sealed class TaskDictationPrompts : IDictationPrompts
{
    public static readonly TaskDictationPrompts Instance = new();

    private TaskDictationPrompts()
    {
    }

    public string SystemInstruction(DictationContext context, bool audio) => DictationPrompt.SystemInstruction(context, audio);

    public string UserText(string text) => DictationPrompt.UserText(text);

    public string AudioUserText => DictationPrompt.AudioUserText;

    public string SchemaName => "dictated_tasks";

    public object GeminiSchema(DictationContext context) => DictationPrompt.GeminiSchema(context);

    public object OpenAiSchema(DictationContext context) => DictationPrompt.OpenAiSchema(context);
}
