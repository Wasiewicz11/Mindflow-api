using Microsoft.Extensions.Logging.Abstractions;
using Mindflow.Api.Services.Reminders;

namespace Mindflow.Api.Tests.Reminders;

public class ReminderDispatcherTests
{
    private sealed class FakeChannel(string name, Func<bool> send) : IReminderChannel
    {
        public string Name => name;
        public int Calls { get; private set; }
        public Task<bool> SendAsync(ReminderMessage message, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(send());
        }
    }

    private static readonly ReminderMessage Message = new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "⏰ X", "Za 10 min · termin dziś 14:00", "dziś 14:00",
        DateTimeOffset.UtcNow, "https://app/zadania/1", "token");

    [Fact]
    public async Task Failing_channel_does_not_stop_the_others()
    {
        var broken = new FakeChannel("email", () => throw new InvalidOperationException("smtp down"));
        var silent = new FakeChannel("push", () => false);
        var app = new FakeChannel("app", () => true);
        var dispatcher = new ReminderDispatcher([broken, silent, app], NullLogger<ReminderDispatcher>.Instance);

        var delivered = await dispatcher.DispatchAsync(Message, CancellationToken.None);

        Assert.Equal(["app"], delivered);
        Assert.Equal((1, 1, 1), (broken.Calls, silent.Calls, app.Calls));
    }

    [Fact]
    public async Task Timing_out_channel_does_not_stop_the_others()
    {
        var slow = new FakeChannel("email", () => throw new TimeoutException("SMTP send timed out."));
        var app = new FakeChannel("app", () => true);
        var dispatcher = new ReminderDispatcher([slow, app], NullLogger<ReminderDispatcher>.Instance);

        var delivered = await dispatcher.DispatchAsync(Message, CancellationToken.None);

        Assert.Equal(["app"], delivered);
    }
}
