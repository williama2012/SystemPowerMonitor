namespace SysPwrMon.Services;

/// Persisted application settings — the three MongoDB connection values.
/// Nullable strings are intentional: an absent value is meaningful state
/// ("not configured"), and we never invent defaults for it.
public sealed record AppSettings(string? MongoUri, string? DatabaseName, string? CollectionName)
{
    /// <summary>All three present, non-whitespace — the sink can use them.</summary>
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(MongoUri) &&
        !string.IsNullOrWhiteSpace(DatabaseName) &&
        !string.IsNullOrWhiteSpace(CollectionName);
}
