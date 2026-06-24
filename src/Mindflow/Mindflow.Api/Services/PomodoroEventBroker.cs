using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Mindflow.Api.Services;

public class PomodoroEventBroker : IPomodoroEventBroker
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, Channel<string>>> _subscribers = new();

    public IPomodoroEventSubscription Subscribe(Guid userId)
    {
        var subscriptionId = Guid.NewGuid();
        var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(8)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
        var userSubscribers = _subscribers.GetOrAdd(userId, _ => new ConcurrentDictionary<Guid, Channel<string>>());
        userSubscribers[subscriptionId] = channel;

        return new Subscription(channel.Reader, () => Remove(userId, subscriptionId));
    }

    public void Publish(Guid userId, string eventType)
    {
        if (!_subscribers.TryGetValue(userId, out var userSubscribers)) return;
        foreach (var channel in userSubscribers.Values)
            channel.Writer.TryWrite(eventType);
    }

    private void Remove(Guid userId, Guid subscriptionId)
    {
        if (!_subscribers.TryGetValue(userId, out var userSubscribers)) return;
        if (userSubscribers.TryRemove(subscriptionId, out var channel))
            channel.Writer.TryComplete();
        if (userSubscribers.IsEmpty)
            _subscribers.TryRemove(userId, out _);
    }

    private sealed class Subscription(ChannelReader<string> events, Action onDispose) : IPomodoroEventSubscription
    {
        private int _disposed;
        public ChannelReader<string> Events { get; } = events;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                onDispose();
        }
    }
}
