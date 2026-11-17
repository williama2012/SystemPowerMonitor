using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SysPwrMon.Services;

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
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            _window = new MainWindow();
            _window.Activate();
        }

        /// <summary>
        /// Window minimize/maximize changes the UI policy, not the data
        /// path — sampling and logging continue (the requirement).
        /// </summary>
        public void SetUiSuspended(bool suspended)
            => Telemetry.IsUiSuspended = suspended;
    }
}
