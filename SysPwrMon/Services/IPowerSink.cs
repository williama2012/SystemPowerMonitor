using SysPwrMon.Models;

namespace SysPwrMon.Services;

/// The log destination. Decoupled from sampling by design: Enqueue must
/// never block (a stalled network call must not delay a sample), so the
/// sink owns its own writer.
public interface IPowerSink : IAsyncDisposable
{
    /// <summary>Raised when a sample could not be written. The message is
    /// user-facing; the event is the only error channel — write failures
    /// must not throw into the sampling path.</summary>
    event EventHandler<string> WriteFailed;

    /// <summary>True once valid settings exist. Enqueue while unconfigured
    /// is a silent no-op — the service is responsible for surfacing the
    /// "logging on but unconfigured" state in the UI.</summary>
    bool IsConfigured { get; }

    /// <summary>Point the sink at a Mongo database/collection, or disable
    /// it when the settings are incomplete. Safe to call while running.</summary>
    void Configure(AppSettings settings);

    /// <summary>Hand over one sample. Returns immediately.</summary>
    void Enqueue(PowerSample sample);
}
