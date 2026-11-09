namespace Mindflow.Api.Services.Ai;

public class AiOptions
{
    public const string SectionName = "Ai";

    public string[] ProviderOrder { get; set; } = ["Gemini", "OpenAi", "RuleBased"];
    public bool EnableLocalFallback { get; set; } = true;
    public int MaxSuggestionsPerRun { get; set; } = 3;
    public int ManualAiDailyLimit { get; set; } = 2;
    public int OwnKeyManualAiDailyLimit { get; set; } = 20;
    public string? KeyEncryptionKey { get; set; }
    public ProviderCredentials Gemini { get; set; } = new() { Model = "gemini-3.5-flash" };
    public ProviderCredentials OpenAi { get; set; } = new() { Model = "gpt-4o-mini" };
    public DictationOptions Dictation { get; set; } = new();
}

public class ProviderCredentials
{
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "";
}

public class DictationOptions
{
    public int DailyLimit { get; set; } = 20;
    public int OwnKeyDailyLimit { get; set; } = 200;
    public int PermitsPerMinute { get; set; } = 6;
    public int MaxAudioBytes { get; set; } = 8 * 1024 * 1024;
    public int MaxTextLength { get; set; } = 5000;
    public int TimeoutSeconds { get; set; } = 60;
    public int MaxTasks { get; set; } = 30;
    public string GeminiFallbackModel { get; set; } = "gemini-3.5-flash-lite";
    public string OpenAiTranscriptionModel { get; set; } = "gpt-4o-mini-transcribe";
}
