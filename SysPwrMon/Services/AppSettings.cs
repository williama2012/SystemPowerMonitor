namespace SysPwrMon.Services;

/// Persisted application settings — the three MongoDB connection values
/// plus the minimize-to-tray behavior toggle. The MongoDB strings are
/// nullable (absent = "not configured", we never invent a default for
/// them); MinimizeToTray defaults false so a fresh/absent key reads as
/// "off" rather than a fabricated state.
public sealed record AppSettings(string? MongoUri, string? DatabaseName, string? CollectionName, bool MinimizeToTray = false)
{
    /// <summary>All three present, non-whitespace — the sink can use them.</summary>
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(MongoUri) &&
        !string.IsNullOrWhiteSpace(DatabaseName) &&
        !string.IsNullOrWhiteSpace(CollectionName);
}
