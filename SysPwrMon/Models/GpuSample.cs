using MongoDB.Bson.Serialization.Attributes;

namespace SysPwrMon.Models;

/// One GPU's contribution to a log entry — exact field names per the
/// required database format. Nullable doubles become BSON null when the
/// sensor is unavailable (e.g. VRAM load on a vendor that lacks the
/// NVML/ADL counter), so readers can tell "unknown" from "zero".
public sealed class GpuSample
{
    [BsonElement("name")]
    public string Name { get; set; } = null!;

    [BsonElement("core_load")]
    public double? CoreLoad { get; set; }

    [BsonElement("core_power")]
    public double? CorePower { get; set; }

    [BsonElement("vram_used")]
    public double? VramUsed { get; set; }

    [BsonElement("vram_load")]
    public double? VramLoad { get; set; }

    // Copy, not bind: the document is frozen at capture time even though
    // the telemetry keeps moving under it.
    public static GpuSample From(GpuSampleData gpu) => new()
    {
        Name = gpu.Name,
        CoreLoad = gpu.CoreLoad,
        CorePower = gpu.CorePower,
        VramUsed = gpu.VramUsedMiB,
        VramLoad = gpu.VramLoad,
    };
}
