using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Mindflow.Api.Services.Telegram;

namespace Mindflow.Api.Tests.Infrastructure;

public class TelegramApiFactory : MindflowApiFactory
{
    public FakeTelegramHandler Telegram { get; } = new();

    protected override IReadOnlyDictionary<string, string> Settings => new Dictionary<string, string>(base.Settings)
    {
        ["Telegram:BotToken"] = "123:abc",
        ["Telegram:BotUsername"] = "mindflow_test_bot",
        ["Telegram:WebhookSecret"] = "hook-secret",
        ["Notifications:FrontendUrl"] = "https://app.test"
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
            services.AddHttpClient<TelegramApi>().ConfigurePrimaryHttpMessageHandler(() => Telegram));
    }
}
