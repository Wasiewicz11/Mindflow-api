using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mindflow.Api.Services.Telegram;
using Mindflow.Api.Tests.Infrastructure;

namespace Mindflow.Api.Tests.Telegram;

public class TelegramLoggingTests(MindflowApiFactory factory) : IClassFixture<MindflowApiFactory>
{
    [LocalDatabaseFact]
    public async Task Bot_token_never_reaches_the_logs()
    {
        var handler = new FakeTelegramHandler();
        var logs = new CapturingLoggerProvider();
        using var app = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Telegram:BotToken"] = "123:abc",
                ["Telegram:BotUsername"] = "mindflow_test_bot"
            }));
            builder.ConfigureLogging(l => { l.SetMinimumLevel(LogLevel.Trace); l.AddProvider(logs); });
            builder.ConfigureTestServices(services =>
                services.AddHttpClient<TelegramApi>().ConfigurePrimaryHttpMessageHandler(() => handler));
        });
        using var scope = app.Services.CreateScope();

        var ok = await scope.ServiceProvider.GetRequiredService<TelegramApi>().SendMessageAsync(1, "hi", null, CancellationToken.None);

        Assert.True(ok);
        Assert.Single(handler.Requests);
        Assert.DoesNotContain(logs.Messages, m => m.Contains("123:abc"));
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<string> Messages { get; } = [];
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);
        public void Dispose() { }

        private sealed class CapturingLogger(CapturingLoggerProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (owner.Messages) owner.Messages.Add(formatter(state, exception));
            }
        }
    }
}
