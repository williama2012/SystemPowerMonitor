namespace SysPwrMon.Services;

/// Persistence boundary for application settings.
/// The registry implementation is required; the abstraction exists so the
/// TelemetryService and SettingsPage never touch Registry directly —
/// they stay testable against an in-memory fake.
public interface ISettingsStore
{
    /// <summary>Load persisted settings; missing values come back as null.</summary>
    AppSettings Load();

    /// <summary>Persist settings. Values of null are removed.</summary>
    void Save(AppSettings settings);
}
