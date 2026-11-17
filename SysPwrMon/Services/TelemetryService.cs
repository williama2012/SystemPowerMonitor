using System.Threading;
using SysPwrMon.Models;

namespace SysPwrMon.Services;

/// The one-and-only clock: a 1 Hz System.Threading.Timer drives
/// sample → maybe-log → maybe-publish for the whole app.
///
/// Why System.Threading.Timer instead of a DispatcherTimer: the requirement
/// is "no UI updates when minimized, but keep logging" — sampling must run
/// at full rate with no visible window. A DispatcherTimer would tie the
/// data path to UI-thread pumping. The tension with XAML is bridged by an
/// event; subscribers marshal to the UI thread themselves.
///
/// Ticks are serialized by a gate: LHM's Computer tree is not safe for
/// concurrent Refresh(), and a System.Threading.Timer may invoke callbacks
/// on different pool threads. A slow tick yields to the next one rather
/// than queue up (one dropped frame beats drift).
public sealed class TelemetryService : IAsyncDisposable
{
    // 1 Hz: readable on a screen, gentle on NVML/RAPL/D3D calls. Faster
    // buys little; slower makes "current" feel stale.
    public const int SampleIntervalMilliseconds = 1000;

    private readonly ITelemetrySource _source;
    private readonly IPowerSink _sink;
    private readonly ISettingsStore _settingsStore;
    private readonly Timer _timer;
    private readonly SemaphoreSlim _tickGate = new(1, 1);

    private volatile bool _loggingEnabled;
    private volatile bool _uiSuspended;

    public TelemetryService(ITelemetrySource source, IPowerSink sink, ISettingsStore settingsStore)
    {
        _source = source;
        _sink = sink;
        _settingsStore = settingsStore;
        _sink.WriteFailed += (sender, message) => LastError = message;

        // Requirement: load saved settings on application launch. The
        // service is the only place all three pieces meet, so it performs
        // the launch-load itself.
        ApplySettings(settingsStore.Load());

        _timer = new Timer(_ => Tick(), state: null, SampleIntervalMilliseconds, SampleIntervalMilliseconds);
    }

    /// <summary>Raised after each tick unless the UI is suspended.</summary>
    public event EventHandler<TelemetrySample>? SnapshotUpdated;

    /// <summary>Latest sensor values as an immutable sample (UI thread
    /// may call this to seed the view; the sampling thread also calls it).</summary>
    public TelemetrySample Read() => _source.Read();

    public bool IsLoggingEnabled
    {
        get => _loggingEnabled;
        set => _loggingEnabled = value;
    }

    /// <summary>True while the window is minimized: suppress UI publication
    /// only — sampling and logging continue (the requirement).</summary>
    public bool IsUiSuspended
    {
        get => _uiSuspended;
        set => _uiSuspended = value;
    }

    /// <summary>One-line UI status; recomputed on demand, not evented.</summary>
    public string Status => _loggingEnabled
        ? (_sink.IsConfigured ? "Logging on" : "Logging on — not configured")
        : "Logging off";

    /// <summary>Last write error, if any. Never cleared — the most recent
    /// failure is what the user should see.</summary>
    public string? LastError { get; private set; }

    /// <summary>Point the sink at the (freshly saved) settings.</summary>
    public void ApplySettings(AppSettings settings)
        => _sink.Configure(settings);

    private void Tick()
    {
        // Try-entrance: a previous tick still inside Refresh() means the
        // hardware is busy; skip this frame instead of queueing behind it.
        if (!_tickGate.Wait(0))
            return;
        try
        {
            _source.Refresh();
            var sample = _source.Read();
            if (_loggingEnabled && _sink.IsConfigured)
                _sink.Enqueue(PowerSample.From(sample));
            if (!_uiSuspended)
                SnapshotUpdated?.Invoke(this, sample);
        }
        finally
        {
            _tickGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        // Order matters: stop ticking first so nothing enqueues after the
        // channel completes, then drain the writer, then close hardware.
        _timer.Dispose();
        await _sink.DisposeAsync();
        _source.Dispose();
    }
}
