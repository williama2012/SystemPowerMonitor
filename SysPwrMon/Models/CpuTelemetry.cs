using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SysPwrMon.Models;

/// CPU telemetry bound to the UI. One stable instance for the process:
/// values mutate in place every sample tick so data bindings stay attached
/// (recreating the object each tick would orphan every TextBlock binding).
/// Thread tension: the timer thread writes while the UI thread reads —
/// doubles are 8-byte aligned on x64, so the worst case is one frame
/// showing a stale value, never a torn one.
public sealed class CpuTelemetry : INotifyPropertyChanged
{
    private string _name = string.Empty;
    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    private double? _load;
    public double? Load
    {
        get => _load;
        set { _load = value; OnPropertyChanged(); }
    }

    private double? _power;
    public double? Power
    {
        get => _power;
        set { _power = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
