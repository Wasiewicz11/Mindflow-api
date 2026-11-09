using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Mindflow.Api.Services.Email;

namespace Mindflow.Api.Tests.Reminders;

public class SmtpEmailSenderTests
{
    [Fact]
    public async Task Unresponsive_server_surfaces_as_timeout_exception()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Email:Smtp:Host"] = "127.0.0.1",
            ["Email:Smtp:Port"] = port.ToString(),
            ["Email:Smtp:From"] = "Mindflow <noreply@mindflow.test>",
            ["Email:Smtp:UseSsl"] = "false"
        }).Build();
        var sender = new SmtpEmailSender(configuration, TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAsync<TimeoutException>(() =>
            sender.SendAsync(new EmailMessage("a@b.pl", "s", "<p>h</p>", "t"), CancellationToken.None));
    }

    [Fact]
    public async Task Caller_cancellation_still_propagates_as_cancellation()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Email:Smtp:Host"] = "127.0.0.1",
            ["Email:Smtp:Port"] = port.ToString(),
            ["Email:Smtp:From"] = "Mindflow <noreply@mindflow.test>",
            ["Email:Smtp:UseSsl"] = "false"
        }).Build();
        var sender = new SmtpEmailSender(configuration);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sender.SendAsync(new EmailMessage("a@b.pl", "s", "<p>h</p>", "t"), cts.Token));
    }
}
