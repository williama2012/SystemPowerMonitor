using SysPwrMon.Models;

namespace SysPwrMon.Services;

/// Hardware telemetry source. Two-phase per tick: Refresh() asks the
/// hardware to recompute its sensor cache, Read() copies the latest values
/// into a fresh immutable <see cref="TelemetrySample"/>. The split exists
/// because the UI-bound models may only be touched on the UI thread (WinUI 3
/// marshals INotifyPropertyChanged through WinRT — a raise from a worker
/// thread faults), so only plain data may cross threads. The source is
/// owned by exactly one worker (TelemetryService's tick); Refresh() is not
/// re-entrant — the service serializes it.
public interface ITelemetrySource : IDisposable
{
    /// <summary>Recompute the hardware's sensor cache for this tick.</summary>
    void Refresh();

    /// <summary>Copy the latest sensor values into a new sample.</summary>
    TelemetrySample Read();
}
