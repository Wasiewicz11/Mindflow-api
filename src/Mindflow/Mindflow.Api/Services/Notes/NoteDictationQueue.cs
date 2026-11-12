using System.Threading.Channels;

namespace Mindflow.Api.Services.Notes;

public class NoteDictationQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });

    public void Enqueue(Guid noteId) => _channel.Writer.TryWrite(noteId);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}
