using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Mindflow.Api.Models.Enums;
using Mindflow.Api.Services.Ai;

namespace Mindflow.Api.Tests.Ai;

public class AiSuggestionOrchestratorTests
{
    private static readonly DaySnapshot Snapshot = new(new DateOnly(2026, 11, 1), []);
    private static readonly AiCredentials UserGemini = new(AiProvider.Gemini, "user-key", "gemini-3.5-flash", AiKeySource.User);
    private static readonly AiCredentials ServerOpenAi = new(AiProvider.OpenAi, "server-key", "gpt-4o-mini", AiKeySource.Server);

    [Fact]
    public async Task Calls_the_matching_provider_with_the_resolved_credentials()
    {
        var gemini = new StubProvider(AiProvider.Gemini);
        var orchestrator = Create(gemini, new StubProvider(null));

        var result = await orchestrator.GenerateAsync(Snapshot, [UserGemini], aiAllowed: true);

        Assert.Equal("Gemini", result.ProviderName);
        Assert.True(result.UsedAi);
        Assert.Same(UserGemini, gemini.Received.Single());
    }

    [Fact]
    public async Task Falls_back_to_the_next_credentials_and_finally_to_local_rules()
    {
        var gemini = new StubProvider(AiProvider.Gemini, fail: true);
        var openAi = new StubProvider(AiProvider.OpenAi, fail: true);
        var local = new StubProvider(null);

        var result = await Create(gemini, openAi, local).GenerateAsync(Snapshot, [UserGemini, ServerOpenAi], aiAllowed: true);

        Assert.Equal("RuleBased", result.ProviderName);
        Assert.False(result.UsedAi);
        Assert.Single(openAi.Received);
    }

    [Fact]
    public async Task Skips_ai_when_the_daily_limit_is_used_up()
    {
        var gemini = new StubProvider(AiProvider.Gemini);

        var result = await Create(gemini, new StubProvider(null)).GenerateAsync(Snapshot, [UserGemini], aiAllowed: false);

        Assert.Empty(gemini.Received);
        Assert.Equal("RuleBased", result.ProviderName);
    }

    [Fact]
    public async Task Without_any_key_only_local_rules_run()
    {
        var result = await Create(new StubProvider(AiProvider.Gemini), new StubProvider(null)).GenerateAsync(Snapshot, [], aiAllowed: true);

        Assert.Equal("RuleBased", result.ProviderName);
    }

    private static AiSuggestionOrchestrator Create(params IAiSuggestionProvider[] providers) =>
        new(providers, Options.Create(new AiOptions()), NullLogger<AiSuggestionOrchestrator>.Instance);

    private sealed class StubProvider(AiProvider? provider, bool fail = false) : IAiSuggestionProvider
    {
        public List<AiCredentials?> Received { get; } = [];
        public string Name => provider?.ToString() ?? "RuleBased";
        public AiProvider? Provider => provider;

        public Task<IReadOnlyList<SuggestionDraft>> GenerateAsync(DaySnapshot snapshot, AiCredentials? credentials, CancellationToken ct = default)
        {
            Received.Add(credentials);
            if (fail) throw new HttpRequestException("down");
            return Task.FromResult<IReadOnlyList<SuggestionDraft>>([new SuggestionDraft("Tytuł", "Treść", [])]);
        }
    }
}
