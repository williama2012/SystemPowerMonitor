using Microsoft.Win32;

namespace SysPwrMon.Services;

/// Windows registry store: HKCU\Software\BigDesignSolutions\SysPwrMon.
/// Why registry (per the requirement) over a config file: settings live
/// per-user in the OS, survive reinstall, and the user can inspect them
/// without digging through app data. Trade-off accepted: registry values
/// are per-machine-per-user, so a multi-profile machine sees one setting
/// set per Windows user — fine for a single-user power monitor.
public sealed class RegistrySettingsStore : ISettingsStore
{
    private const string SubKeyPath = @"Software\BigDesignSolutions\SysPwrMon";
    private const string UriValueName = "MongoUri";
    private const string DatabaseValueName = "DatabaseName";
    private const string CollectionValueName = "CollectionName";
    private const string MinimizeToTrayValueName = "MinimizeToTray";
    private const string WindowBoundsValueName = "WindowBounds";

    public AppSettings Load()
    {
        // OpenSubKey throws only on permission faults on HKCU subkeys —
        // which cannot happen for our own key; treat missing as "blank".
        using var key = Registry.CurrentUser.OpenSubKey(SubKeyPath);
        return key is null
            ? new AppSettings(null, null, null)
            : new AppSettings(
                Read(key, UriValueName),
                Read(key, DatabaseValueName),
                Read(key, CollectionValueName),
                ReadBool(key, MinimizeToTrayValueName),
                ReadBounds(key, WindowBoundsValueName));
    }

    public void Save(AppSettings settings)
    {
        using var key = Registry.CurrentUser.CreateSubKey(SubKeyPath);
        Write(key, UriValueName, settings.MongoUri);
        Write(key, DatabaseValueName, settings.DatabaseName);
        Write(key, CollectionValueName, settings.CollectionName);
        WriteBool(key, MinimizeToTrayValueName, settings.MinimizeToTray);
        WriteBounds(key, WindowBoundsValueName, settings.WindowBounds);
    }

    private static string? Read(RegistryKey key, string name)
        => key.GetValue(name) as string; // absent → null, never a default

    private static void Write(RegistryKey key, string name, string? value)
    {
        if (value is null)
            key.DeleteValue(name, throwOnMissingValue: false);
        else
            key.SetValue(name, value);
    }

    // The minimize-to-tray toggle is a bool, stored as a DWORD (1/0).
    // Absent == off: mirrors the string pattern (absent value = default),
    // so a key the user never set reads "off", not a fabricated "on".
    private static bool ReadBool(RegistryKey key, string name)
        => key.GetValue(name) is int value && value != 0;

    private static void WriteBool(RegistryKey key, string name, bool value)
    {
        if (value)
            key.SetValue(name, 1, RegistryValueKind.DWord);
        else
            key.DeleteValue(name, throwOnMissingValue: false);
    }

    // Window bounds as "x,y,w,h" in one value: atomic, so a crash can't
    // leave a half-old window. Unparseable == "not saved" (the app must
    // launch, and the next close rewrites the value) — the same absent-
    // default policy as the string values above.
    private static WindowBounds? ReadBounds(RegistryKey key, string name)
    {
        var raw = key.GetValue(name) as string;
        if (raw is null)
            return null;

        var parts = raw.Split(',');
        return parts.Length is 4
            && int.TryParse(parts[0], out var x)
            && int.TryParse(parts[1], out var y)
            && int.TryParse(parts[2], out var w)
            && int.TryParse(parts[3], out var h)
            ? new WindowBounds(x, y, w, h)
            : null;
    }

    private static void WriteBounds(RegistryKey key, string name, WindowBounds? bounds)
    {
        if (bounds is null)
            key.DeleteValue(name, throwOnMissingValue: false);
        else
            key.SetValue(name, $"{bounds.X},{bounds.Y},{bounds.Width},{bounds.Height}");
    }
}
