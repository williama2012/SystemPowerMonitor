using Microsoft.UI.Xaml;
using SysPwrMon.Native;
using SysPwrMon.Services;
using WinRT.Interop;

namespace SysPwrMon
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// This is the composition root: the only place the service graph is built.
    /// </summary>
    public partial class App : Application
    {
        private Window? _window;

        // One instance for the process: created on first touch, so the
        // pages resolve it without a DI container (and the hardware opens
        // exactly once per app run).
        private TelemetryService? _telemetry;
        private ISettingsStore? _settingsStore;

        // Created here (not in Telemetry) so the page and the service
        // share one store instance — same path either way, but one object
        // is cheaper to reason about.
        public ISettingsStore SettingsStore => _settingsStore ??= new RegistrySettingsStore();

        public TelemetryService Telemetry
            => _telemetry ??= new TelemetryService(
                new LibreHardwareMonitorSource(),
                new MongoPowerSink(),
                SettingsStore);

        /// <summary>
        /// Whether minimize tucks the window into the system tray. Read from
        /// the store (not cached) so the window reflects a live toggle: the
        /// behavior is decided at each minimize, and a registry read is cheap.
        /// </summary>
        public bool MinimizeToTrayEnabled => SettingsStore.Load().MinimizeToTray;

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            _window = new MainWindow();
            _window.Activate();

            // Unpackaged: the running taskbar/title-bar icon is NOT taken from
            // the .exe file icon (ApplicationIcon) nor Package.appxmanifest
            // (ignored with MSIX tooling off) — Windows shows the default. Set
            // it on the HWND instead. Same .ico the .exe uses, so the running
            // app matches its file icon.
            WindowIcon.SetWindowIcon(
                WindowNative.GetWindowHandle(_window),
                Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));

            // Window bounds: restore the user's last layout if one was saved
            // on a previous close; otherwise the first-run default. The save
            // reads AppWindow.Position/Size, the restore uses MoveAndResize —
            // a same-units round trip, so DPI scaling stays WinUI's.
            var savedBounds = SettingsStore.Load().WindowBounds;
            var bounds = savedBounds is { IsRestorable: true }
                ? savedBounds
                : DefaultWindowBounds;
            _window.AppWindow.MoveAndResize(
                new Windows.Graphics.RectInt32(bounds.X, bounds.Y, bounds.Width, bounds.Height));
        }

        /// First-run window bounds — used until the first close has saved a
        /// layout. (MoveAndResize runs every launch; the OS never persists a
        /// window's own placement, so without the saved value the window
        /// would open here on every run.)
        private static readonly WindowBounds DefaultWindowBounds = new(0, 0, 600, 400);

        /// Persist the window's current bounds (called on window close).
        /// Load-modify-save through the same store the pages write: one Save
        /// carries the whole settings set, so no field is lost.
        public void SaveWindowBounds(Windows.Graphics.PointInt32 position, Windows.Graphics.SizeInt32 size)
            => SettingsStore.Save(SettingsStore.Load() with
            {
                WindowBounds = new WindowBounds(position.X, position.Y, size.Width, size.Height)
            });

        /// <summary>
        /// Window minimize/maximize changes the UI policy, not the data
        /// path — sampling and logging continue (the requirement).
        /// </summary>
        public void SetUiSuspended(bool suspended)
            => Telemetry.IsUiSuspended = suspended;
    }
}
