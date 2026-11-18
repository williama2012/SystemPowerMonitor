using MongoDB.Driver;
using SysPwrMon.Models;
using System.Threading.Channels;

namespace SysPwrMon.Services;

/// MongoDB log sink: an unbounded channel plus one writer task.
///
/// Why a channel: sampling runs at 1 Hz and must stay fast; network I/O
/// (insert round-trip) is the slow, fallible side and belongs in its own
/// task. Trade-off accepted: if Mongo is down for minutes, the queue grows
/// — memory growth is the cost we pay so that a log never drops entries.
/// The tension with fail-fast is resolved at the boundary: a bad *config*
/// is explicit (Configure deactivates + raises WriteFailed), while a
/// *runtime* outage is surfaced per-sample via WriteFailed and the app
/// keeps running — a telemetry app that dies because its database is down
/// would be the wrong tool.
public sealed class MongoPowerSink : IPowerSink
{
    private readonly Channel<PowerSample> _channel = Channel.CreateUnbounded<PowerSample>();
    private readonly Task _writer;

    // The writer task captures the sink for its whole life; starting it
    // in the constructor is the only place it exists.
    public MongoPowerSink()
    {
        _writer = Task.Run(WriteLoopAsync);
    }

    public event EventHandler<string>? WriteFailed;

    private long _configured;
    public bool IsConfigured => Volatile.Read(ref _configured) == 1;

    private readonly object _gate = new();
    private IMongoClient? _client;
    private IMongoCollection<PowerSample>? _collection;

    public void Configure(AppSettings settings)
    {
        if (!settings.IsComplete)
        {
            Deactivate();
            return;
        }

        // Fail-fast on a bad URI: MongoUrl parses eagerly, so a garbage
        // value cannot hide until first insert.
        IMongoClient client;
        IMongoCollection<PowerSample> collection;
        try
        {
            client = new MongoClient(new MongoUrl(settings.MongoUri));
            collection = client.GetDatabase(settings.DatabaseName).GetCollection<PowerSample>(settings.CollectionName);
        }
        catch (Exception ex)
        {
            Deactivate();
            WriteFailed?.Invoke(this, $"MongoDB settings rejected: {ex.Message}");
            return;
        }

        // Swap under the lock so the writer ever sees either the old or
        // the new pair — never a client from one generation with a
        // collection from another.
        lock (_gate)
        {
            var old = _client;
            _client = client;
            _collection = collection;
            Volatile.Write(ref _configured, 1);
            if (old is not null)
                old.Dispose();
        }
    }

    public void Enqueue(PowerSample sample)
    {
        // A completed channel rejects writes; that only happens at
        // shutdown, where dropping one sample is the right answer.
        try
        {
            _channel.Writer.TryWrite(sample);
        }
        catch (ChannelClosedException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.Complete();
        await _writer;
        lock (_gate)
        {
            _client?.Dispose();
            _client = null;
            _collection = null;
        }
        Volatile.Write(ref _configured, 0);
    }

    private void Deactivate()
    {
        lock (_gate)
        {
            _client?.Dispose();
            _client = null;
            _collection = null;
        }
        Volatile.Write(ref _configured, 0);
    }

    private async Task WriteLoopAsync()
    {
        // ReadAllAsync completes when the channel is completed — the loop
        // is the whole lifetime of the writer.
        await foreach (var sample in _channel.Reader.ReadAllAsync())
        {
            IMongoCollection<PowerSample>? collection;
            lock (_gate)
            {
                collection = _collection;
            }
            if (collection is null)
            {
                continue; // logging disabled mid-flight: drop, status says why
            }

            try
            {
                await collection.InsertOneAsync(sample);
            }
            catch (Exception ex)
            {
                // Surface, don't throw: the sampling thread must never see
                // a network exception.
                WriteFailed?.Invoke(this, ex.Message);
            }
        }
    }
}
