using LibreHardwareMonitor.Hardware;
using SysPwrMon.Models;

namespace SysPwrMon.Services;

/// LibreHardwareMonitor is the hardware source of truth.
///
/// Architecture: the LHM Computer tree is opened once at construction
/// (its enumeration is expensive and one-shot) and sampled per tick
/// against the same hardware objects. We partition the tree into a CPU
/// and a GPU list and keep them for the app's lifetime.
///
/// Trade-off: holding the Computer means LHM's background updater threads
/// live for the whole process — that is LHM's designed pattern (its own
/// GUI works this way), and at 1 Hz the driver-call cost is negligible.
///
/// Constraint (do NOT change without revisiting the assumption): LHM 0.9.6
/// exposes one generic ISensor surface per hardware, so sensors are
/// addressed by (Name, SensorType) pairs below. Those names were verified
/// against the v0.9.6 source and against this machine's live hardware
/// (Intel 13th gen + NVIDIA NVML). If LHM is upgraded, this file must be
/// re-verified against the new tag — master-branch naming has drifted.
public sealed class LibreHardwareMonitorSource : ITelemetrySource
{
    // Sensor names — v0.9.6, verified live on hardware (see class comment).
    private const string CpuTotalLoadName = "CPU Total";
    private const string CpuPackagePowerName = "CPU Package";
    private const string GpuCoreLoadName = "GPU Core";
    private const string GpuPackagePowerName = "GPU Package";
    private const string GpuPowerLoadName = "GPU Power"; // load-% on some vendors; not power
    private const string GpuMemoryLoadName = "GPU Memory";
    private const string GpuMemoryUsedName = "GPU Memory Used";
    private const string GpuMemoryFreeName = "GPU Memory Free";
    private const string GpuMemoryTotalName = "GPU Memory Total";
    private const string D3DMemoryUsedName = "D3D Dedicated Memory Used"; // non-NVML fallback
    private const string D3DMemoryFreeName = "D3D Dedicated Memory Free";
    private const string D3DMemoryTotalName = "D3D Dedicated Memory Total";
    private const string UnknownCpuName = "CPU";

    private readonly Computer _computer;
    private readonly IHardware? _cpu;
    private readonly IHardware[] _gpus;

    public LibreHardwareMonitorSource()
    {
        // Enable flags are read during Open()'s group addition, so they
        // must be set before Open(). Only CPU + GPU groups are enumerated
        // — every other group would add drivers/callouts we never read.
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
        };
        _computer.Open();

        _cpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
        _gpus = _computer.Hardware.Where(h => IsGpuType(h.HardwareType)).ToArray();
    }

    public void Refresh()
    {
        // Update() recomputes each sensor's cached value for this tick;
        // reading without it would serve last tick's numbers. One Update
        // per hardware per tick is the contract LHM's own GUI follows.
        // No UI-bound model is touched here: WinUI 3 marshals
        // INotifyPropertyChanged through WinRT, so a raise from this
        // worker thread faults — the UI thread copies values from Read().
        if (_cpu is not null)
            _cpu.Update();
        foreach (var gpu in _gpus)
            gpu.Update();
    }

    public TelemetrySample Read()
    {
        // "GPU Package" is the board total power (NVML); the "GPU Power"
        // *load* sensor is a percent of power limit, not watts — never
        // fall back to it.
        var gpus = _gpus.Select(gpu => new GpuSampleData(
            gpu.Name,
            Find(gpu, GpuCoreLoadName, SensorType.Load),
            Find(gpu, GpuPackagePowerName, SensorType.Power),
            Find(gpu, GpuMemoryUsedName, SensorType.SmallData) ?? Find(gpu, D3DMemoryUsedName, SensorType.SmallData),
            Find(gpu, GpuMemoryFreeName, SensorType.SmallData) ?? Find(gpu, D3DMemoryFreeName, SensorType.SmallData),
            Find(gpu, GpuMemoryTotalName, SensorType.SmallData) ?? Find(gpu, D3DMemoryTotalName, SensorType.SmallData),
            Find(gpu, GpuMemoryLoadName, SensorType.Load))).ToArray();

        return new TelemetrySample(
            _cpu?.Name ?? UnknownCpuName,
            Find(_cpu, CpuTotalLoadName, SensorType.Load),
            Find(_cpu, CpuPackagePowerName, SensorType.Power),
            gpus);
    }

    private static bool IsGpuType(HardwareType type)
        => type is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel;

    private static float? Find(IHardware? hardware, string name, SensorType sensorType)
    {
        if (hardware is null)
            return null;
        foreach (var sensor in hardware.Sensors)
            if (sensor.Name == name && sensor.SensorType == sensorType)
                return sensor.Value;
        return null;
    }

    public void Dispose() => _computer.Close();
}
