using System.Text;
using LibreHardwareMonitor.Hardware;
using SysPwrMon.Models;

namespace SysPwrMon.Services;

/// Hardware source of truth.
///
/// CPU is always LibreHardwareMonitor (RAPL package power via PawIO, CPU
/// total load). GPUs come from direct NVML first, with LibreHardwareMonitor as
/// the fallback: LHM's NVML wrapper mis-attributes per-adapter memory on
/// multi-GPU systems (both adapters can read one shared memory block), so when
/// an NVIDIA driver is present we read NVML directly — values then match
/// nvidia-smi exactly. The fallback serves non-NVIDIA (AMD/Intel) hardware and
/// boxes with no NVIDIA driver.
///
/// The GPU source is decided ONCE, at construction, on NVML availability
/// (init + at least one device). That is the "attempt NVML first, fall back to
/// LHM if the checks fail" contract: it is a source *selection*, not a per-tick
/// or per-device mix, so a sample never blends two backends.
///
/// Limitation (by design): in NVML mode the GPU list is the NVIDIA devices
/// only; a mixed NVIDIA+AMD box in NVML mode omits the non-NVIDIA card — the
/// LHM path is what serves non-NVIDIA hardware.
///
/// Constraint (do NOT change without revisiting the assumption): the LHM sensor
/// names below are addressed as (Name, SensorType) pairs — LHM 0.9.6 exposes
/// one generic ISensor surface per hardware. They were verified against the
/// v0.9.6 source and this machine's live hardware (Intel 13th gen + NVIDIA
/// NVML). If LHM is upgraded, the LHM path must be re-verified against the
/// new tag (master-branch naming has drifted); the NVML path is unaffected.
public sealed class LibreHardwareMonitorSource : ITelemetrySource
{
    // LHM sensor names — v0.9.6, verified live on hardware (see class comment).
    private const string CpuTotalLoadName = "CPU Total";
    private const string CpuPackagePowerName = "CPU Package";
    private const string GpuCoreLoadName = "GPU Core";
    private const string GpuPackagePowerName = "GPU Package";
    private const string GpuMemoryLoadName = "GPU Memory";
    private const string GpuMemoryUsedName = "GPU Memory Used";
    private const string GpuMemoryFreeName = "GPU Memory Free";
    private const string GpuMemoryTotalName = "GPU Memory Total";
    private const string D3DMemoryUsedName = "D3D Dedicated Memory Used";   // non-NVML fallback
    private const string D3DMemoryFreeName = "D3D Dedicated Memory Free";
    private const string D3DMemoryTotalName = "D3D Dedicated Memory Total";
    private const string UnknownCpuName = "CPU";

    // VRAM is reported in mebibytes; NVML returns bytes.
    private const double BytesPerMebibyte = 1024.0 * 1024.0;

    private readonly Computer _computer;
    private readonly IHardware? _cpu;
    private readonly IHardware[] _gpus;

    // NVML lifecycle: one init for the process lifetime, one shutdown on
    // dispose. It is a separate P/Invoke layer (Nvml.cs) from the LHM Computer
    // tree above — NVML reads live and needs no per-tick refresh.
    private readonly bool _nvmlReady;        // init OK and >= 1 device → GPU source
    private readonly int _nvmlDeviceCount;
    private readonly bool _nvmlInitialized; // nvmlInit succeeded → must shutdown

    public LibreHardwareMonitorSource()
    {
        // Decide the GPU source before opening the LHM Computer: NVML is the
        // primary GPU source when usable, otherwise LHM serves the GPUs. NVML
        // init is a process-level call (matches the GpuMonitor GpuReader: one
        // init, not per tick).
        _nvmlInitialized = Nvml.nvmlInit() == Nvml.Success;
        _nvmlDeviceCount = 0;
        _nvmlReady = false;
        if (_nvmlInitialized && Nvml.nvmlDeviceGetCount(out var count) == Nvml.Success)
        {
            _nvmlDeviceCount = (int)count;
            _nvmlReady = _nvmlDeviceCount > 0;
        }

        // IsCpuEnabled is always on (CPU is LHM). IsGpuEnabled stays on so the
        // LHM GPU tree exists as the fallback; it is only *refreshed* when it
        // is the active source (see Refresh) — never read in NVML mode.
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
        // CPU is always LHM: Update() recomputes each sensor's cached value for
        // this tick (reading without it serves last tick's numbers). NVML needs
        // no refresh — its reads are live. LHM GPUs are refreshed only when they
        // are the active source. No UI-bound model is touched here: WinUI 3
        // marshals INotifyPropertyChanged through WinRT, so a raise from this
        // worker thread faults — the UI thread copies values from Read().
        if (_cpu is not null)
            _cpu.Update();
        if (!_nvmlReady)
            foreach (var gpu in _gpus)
                gpu.Update();
    }

    public TelemetrySample Read()
    {
        return new TelemetrySample(
            _cpu?.Name ?? UnknownCpuName,
            Find(_cpu, CpuTotalLoadName, SensorType.Load),
            Find(_cpu, CpuPackagePowerName, SensorType.Power),
            _nvmlReady ? ReadGpusNvml() : ReadGpusLhm());
    }

    /// <summary>
    /// NVML is the primary GPU source: values match nvidia-smi and report
    /// per-adapter memory correctly on multi-GPU systems.
    /// TENSION (do not conflate): VramLoad is "VRAM in use" = used/total, matching
    /// LHM's "GPU Memory" load sensor. NVML's memory-utilization rate (the
    /// second out of nvmlDeviceGetUtilizationRates) is a *controller busyness*
    /// percentage — a different metric — so VramLoad is computed from the memory
    /// figures, not the utilization call.
    /// </summary>
    private GpuSampleData[] ReadGpusNvml()
    {
        var gpus = new GpuSampleData[_nvmlDeviceCount];
        for (uint i = 0; i < _nvmlDeviceCount; i++)
        {
            // An index that vanished this tick (device removed) is unknown.
            if (Nvml.nvmlDeviceGetHandleByIndex(i, out var handle) != Nvml.Success)
                continue;

            var name = new StringBuilder(256);
            string cardName = Nvml.nvmlDeviceGetName(handle, name, 256) == Nvml.Success
                ? name.ToString()
                : string.Empty;

            double? vramUsed = null, vramFree = null, vramMax = null, vramLoad = null;
            if (Nvml.nvmlDeviceGetMemoryInfo(handle, out var mem) == Nvml.Success)
            {
                vramUsed = ToMebibytes(mem.used);
                vramFree = ToMebibytes(mem.free);
                vramMax = ToMebibytes(mem.total);
                vramLoad = mem.total > 0 ? mem.used / (double)mem.total * 100.0 : null;
            }

            double? coreLoad = null;
            if (Nvml.nvmlDeviceGetUtilizationRates(handle, out var core, out _) == Nvml.Success)
                coreLoad = core;

            double? corePower = null;
            if (Nvml.nvmlDeviceGetPowerUsage(handle, out var watts) == Nvml.Success)
                corePower = (double)watts / 1000;

            gpus[i] = new GpuSampleData(cardName, coreLoad, corePower, vramUsed, vramFree, vramMax, vramLoad);
        }
        return gpus;
    }

    /// <summary>
    /// Fallback: LHM's sensor tree (non-NVIDIA hardware, or no NVIDIA driver).
    /// "GPU Package" is the board total power; the "GPU Power" (*load*) sensor
    /// is a percent of power limit, not watts — never fall back to it.
    /// </summary>
    private GpuSampleData[] ReadGpusLhm() => _gpus.Select(gpu => new GpuSampleData(
        gpu.Name,
        Find(gpu, GpuCoreLoadName, SensorType.Load),
        Find(gpu, GpuPackagePowerName, SensorType.Power),
        Find(gpu, GpuMemoryUsedName, SensorType.SmallData) ?? Find(gpu, D3DMemoryUsedName, SensorType.SmallData),
        Find(gpu, GpuMemoryFreeName, SensorType.SmallData) ?? Find(gpu, D3DMemoryFreeName, SensorType.SmallData),
        Find(gpu, GpuMemoryTotalName, SensorType.SmallData) ?? Find(gpu, D3DMemoryTotalName, SensorType.SmallData),
        Find(gpu, GpuMemoryLoadName, SensorType.Load))).ToArray();

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

    private static double ToMebibytes(ulong bytes) => bytes / BytesPerMebibyte;

    public void Dispose()
    {
        _computer.Close();
        if (_nvmlInitialized)
            Nvml.nvmlShutdown();
    }
}
