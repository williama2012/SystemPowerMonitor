namespace SysPwrMon.Models;

/// One tick's hardware values as an immutable plain copy — deliberately
/// NOT INotifyPropertyChanged. This type is the only thing allowed to
/// cross from the sampling thread to the UI thread: the UI-bound models
/// (CpuTelemetry/GpuTelemetry) are mutated from a sample, on the UI thread,
/// where WinUI 3's binding engine may raise PropertyChanged.
public sealed record TelemetrySample(
    string CpuName,
    double? CpuLoad,
    double? CpuPower,
    IReadOnlyList<GpuSampleData> Gpus);

/// One GPU's row inside a <see cref="TelemetrySample"/>. VRAM is mebibytes,
/// loads are percentages.
public sealed record GpuSampleData(
    string Name,
    double? CoreLoad,
    double? CorePower,
    double? VramUsedMiB,
    double? VramFreeMiB,
    double? VramMaxMiB,
    double? VramLoad);
