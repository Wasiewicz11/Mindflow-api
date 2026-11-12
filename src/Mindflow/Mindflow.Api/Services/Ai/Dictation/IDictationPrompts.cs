namespace Mindflow.Api.Services.Ai.Dictation;

public interface IDictationPrompts
{
    string SystemInstruction(DictationContext context, bool audio);
    string UserText(string text);
    string AudioUserText { get; }
    string SchemaName { get; }
    object GeminiSchema(DictationContext context);
    object OpenAiSchema(DictationContext context);
}
