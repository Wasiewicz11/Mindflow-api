using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Mindflow.Api.Services.Ai.Providers;

namespace Mindflow.Api.Tests.Infrastructure;

public class AiApiFactory : MindflowApiFactory
{
    public const string ServerGeminiKey = "server-gemini-key-0000";

    public FakeAiHandler Ai { get; } = new();

    protected override IReadOnlyDictionary<string, string> Settings => new Dictionary<string, string>(base.Settings)
    {
        ["Ai:KeyEncryptionKey"] = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray()),
        ["Ai:Gemini:ApiKey"] = ServerGeminiKey,
        ["Ai:Gemini:Model"] = "gemini-3.5-flash",
        ["Ai:Dictation:DailyLimit"] = "3",
        ["Ai:Dictation:LiteDailyLimit"] = "2",
        ["Ai:Dictation:PermitsPerMinute"] = "100"
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.AddHttpClient<GeminiApi>().ConfigurePrimaryHttpMessageHandler(() => Ai);
            services.AddHttpClient<OpenAiApi>().ConfigurePrimaryHttpMessageHandler(() => Ai);
        });
    }
}
