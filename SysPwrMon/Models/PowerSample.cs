using MongoDB.Bson.Serialization.Attributes;

namespace SysPwrMon.Models;

/// The MongoDB log document — the exact required format:
/// { timestamp, total_power, cpu_load, cpu_power, gpus:[{name, core_load,
/// core_power, vram_used, vram_load}] }.
/// total_power is the watts the machine is spending on compute: CPU package
/// plus every GPU package. Null terms are dropped; if every term is null
/// (no power sensors at all) total_power is null, not 0 — 0 W would be a
/// false claim.
public sealed class PowerSample
{
    [BsonElement("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [BsonElement("total_power")]
    public double? TotalPower { get; set; }

    [BsonElement("cpu_load")]
    public double? CpuLoad { get; set; }

    [BsonElement("cpu_power")]
    public double? CpuPower { get; set; }

    [BsonElement("gpus")]
    public List<GpuSample> Gpus { get; set; } = new();

    public static PowerSample From(TelemetrySample sample)
    {
        double total = 0;
        bool anyPower = false;
        void AddPower(double? watts)
        {
            if (watts.HasValue)
            {
                total += watts.Value;
                anyPower = true;
            }
        }

        AddPower(sample.CpuPower);
        foreach (var gpu in sample.Gpus)
            AddPower(gpu.CorePower);

        return new PowerSample
        {
            Timestamp = DateTime.UtcNow,
            CpuLoad = sample.CpuLoad,
            CpuPower = sample.CpuPower,
            TotalPower = anyPower ? total : null,
            Gpus = sample.Gpus.Select(GpuSample.From).ToList(),
        };
    }
}
