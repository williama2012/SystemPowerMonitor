using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SysPwrMon.Models;

/// Per-GPU telemetry bound to the UI. Same in-place mutation contract as
/// CpuTelemetry: one stable instance per GPU, values refreshed each tick.
/// VRAM values are mebibytes, matching LibreHardwareMonitor's unit.
public sealed class GpuTelemetry : INotifyPropertyChanged
{
    public string Name { get; set; } = string.Empty;

    private double? _coreLoad;
    public double? CoreLoad
    {
        get => _coreLoad;
        set { _coreLoad = value; OnPropertyChanged(); }
    }

    private double? _corePower;
    public double? CorePower
    {
        get => _corePower;
        set { _corePower = value; OnPropertyChanged(); }
    }

    private double? _vramUsedMiB;
    public double? VramUsedMiB
    {
        get => _vramUsedMiB;
        set { _vramUsedMiB = value; OnPropertyChanged(); }
    }

    private double? _vramFreeMiB;
    public double? VramFreeMiB
    {
        get => _vramFreeMiB;
        set { _vramFreeMiB = value; OnPropertyChanged(); }
    }

    private double? _vramMaxMiB;
    public double? VramMaxMiB
    {
        get => _vramMaxMiB;
        set { _vramMaxMiB = value; OnPropertyChanged(); }
    }

    private double? _vramLoad;
    public double? VramLoad
    {
        get => _vramLoad;
        set { _vramLoad = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
