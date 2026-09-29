using System.Threading.Channels;

namespace ProdvisorAi.Demo.Services;

/// <summary>
/// In-memory queue of job IDs. The job itself (and its state) lives in SQLite, so on restart
/// unfinished jobs are simply re-queued (see JobWorker). A product version could swap this for
/// a durable queue without changing the API.
/// </summary>
public sealed class JobQueue
{
    readonly Channel<string> _channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });

    public ValueTask EnqueueAsync(string jobId, CancellationToken ct = default) => _channel.Writer.WriteAsync(jobId, ct);

    public IAsyncEnumerable<string> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}
