namespace Mindflow.Api.Services.Ai;

public class AiOptions
{
    public const string SectionName = "Ai";

    public string[] ProviderOrder { get; set; } = ["Gemini", "OpenAi", "RuleBased"];
    public bool EnableLocalFallback { get; set; } = true;
    public int MaxSuggestionsPerRun { get; set; } = 3;
    public ProviderCredentials Gemini { get; set; } = new() { Model = "gemini-2.0-flash" };
    public ProviderCredentials OpenAi { get; set; } = new() { Model = "gpt-4o-mini" };
}

public class ProviderCredentials
{
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "";
}
