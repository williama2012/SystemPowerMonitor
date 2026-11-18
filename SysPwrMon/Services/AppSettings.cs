namespace SysPwrMon.Services;

/// Persisted application settings — the three MongoDB connection values,
/// the minimize-to-tray behavior toggle, and the window bounds saved on
/// the last close. The MongoDB strings are nullable (absent = "not
/// configured", we never invent a default for them); MinimizeToTray
/// defaults false so a fresh/absent key reads as "off" rather than a
/// fabricated state. WindowBounds is null until the first close —
/// "no saved layout yet" is a state, not corruption.
public sealed record AppSettings(string? MongoUri, string? DatabaseName, string? CollectionName, bool MinimizeToTray = false, WindowBounds? WindowBounds = null)
{
    /// <summary>All three present, non-whitespace — the sink can use them.</summary>
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(MongoUri) &&
        !string.IsNullOrWhiteSpace(DatabaseName) &&
        !string.IsNullOrWhiteSpace(CollectionName);
}

/// A window bounds snapshot — top-left corner (X, Y) and size (Width,
/// Height) in the units AppWindow reports: read from AppWindow.Position
/// / AppWindow.Size on close, restored via AppWindow.MoveAndResize at
/// launch. Same-units round trip, so DPI scaling stays WinUI's concern.
/// Stored as ONE registry value ("x,y,w,h"): four separate values could
/// tear into a half-old window across a crash between writes.
public sealed record WindowBounds(int X, int Y, int Width, int Height)
{
    /// <summary>Usable for restore — a non-positive size is a corrupt value
    /// (MoveAndResize rejects it), so it reads as "not saved", not a layout.</summary>
    public bool IsRestorable => Width > 0 && Height > 0;
}
