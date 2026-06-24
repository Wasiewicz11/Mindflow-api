using System.Threading.Channels;

namespace Mindflow.Api.Services;

public interface IPomodoroEventSubscription : IDisposable
{
    ChannelReader<string> Events { get; }
}

public interface IPomodoroEventBroker
{
    IPomodoroEventSubscription Subscribe(Guid userId);
    void Publish(Guid userId, string eventType);
}
