using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SysPwrMon.Models;
using SysPwrMon.Services;

namespace SysPwrMon.Pages;

// Thin view over TelemetryService: no decisions live here — the sampling
// cadence, the minimized policy, and log state all sit in the service.
// The page owns the UI-bound models (CpuTelemetry/GpuTelemetry): created
// from the first sample, then mutated on the UI thread only. That is a
// WinUI 3 constraint, not preference — PropertyChanged crosses into the
// XAML binding through WinRT and may only be raised from the UI thread.
public sealed partial class HomePage : Page
{
    private readonly TelemetryService _telemetry;
    private readonly CpuTelemetry _cpu = new();
    private readonly GpuTelemetry[] _gpus;
    private DispatcherQueue? _dispatcherQueue;

    public HomePage()
    {
        InitializeComponent();
        _telemetry = ResolveTelemetry();

        // First sample seeds the stable binding objects. The GPU set is
        // fixed for the process (the source holds one hardware object per
        // adapter); if it ever changed, this page must be rebuilt — do not
        // start appending to _gpus.
        var initial = _telemetry.Read();
        _gpus = initial.Gpus
            .Select(g => new GpuTelemetry { Name = g.Name })
            .ToArray();

        CpuCard.DataContext = _cpu;
        GpuItemsControl.ItemsSource = _gpus;
        ApplySample(initial);
    }

    private static TelemetryService ResolveTelemetry()
        => Application.Current is App app
            ? app.Telemetry
            : throw new InvalidOperationException("Expected the SysPwrMon application instance.");

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        // UI thread: capture the dispatch queue (the only thread where the
        // XAML surface may be read) and sync the checkbox.
        _dispatcherQueue = DispatcherQueue;
        LoggingCheckBox.IsChecked = _telemetry.IsLoggingEnabled;
        _telemetry.SnapshotUpdated += OnSnapshotUpdated;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
        => _telemetry.SnapshotUpdated -= OnSnapshotUpdated;

    private void LoggingCheckBox_Checked(object sender, RoutedEventArgs e)
        => _telemetry.IsLoggingEnabled = true;

    private void LoggingCheckBox_Unchecked(object sender, RoutedEventArgs e)
        => _telemetry.IsLoggingEnabled = false;

    private void OnSnapshotUpdated(object? sender, TelemetrySample sample)
    {
        // The service raises this from its timer thread; the queue is
        // captured on the UI thread in OnNavigatedTo so no XAML property is
        // touched off-thread.
        var queue = _dispatcherQueue;
        if (queue is null)
            return; // first tick before the page attached — nothing to draw.
        queue.TryEnqueue(DispatcherQueuePriority.Normal, () => ApplySample(sample));
    }

    // UI-thread only: every assignment here may raise PropertyChanged.
    private void ApplySample(TelemetrySample sample)
    {
        try
        {
            _cpu.Name = sample.CpuName;
            _cpu.Load = sample.CpuLoad;
            _cpu.Power = sample.CpuPower;
            for (int i = 0; i < _gpus.Length; i++)
            {
                var gpu = _gpus[i];
                var data = sample.Gpus[i];
                gpu.CoreLoad = data.CoreLoad;
                gpu.CorePower = data.CorePower;
                gpu.VramUsedMiB = data.VramUsedMiB;
                gpu.VramFreeMiB = data.VramFreeMiB;
                gpu.VramMaxMiB = data.VramMaxMiB;
                gpu.VramLoad = data.VramLoad;
            }
            StatusTextBlock.Text = string.IsNullOrEmpty(_telemetry.LastError)
                ? _telemetry.Status
                : $"{_telemetry.Status} — {_telemetry.LastError}";

            var totalPower = sample.CpuPower;
            totalPower += _gpus.Select(g => g.CorePower).Where(p => p.HasValue).Select(p => p.Value).Sum();
            SystemTotalPower.Text = $"{totalPower:0.#} W";
        }
        catch (Exception ex)
        {
            // Defensive: the service should never raise a sample with a
            // different GPU count than the first sample, but if it does, the
            // page is in an inconsistent state. Log and show the error.\
            try
            {
                //TODO: Log the error to system logs.
                StatusTextBlock.Text = $"Telemetry sample mismatch: {ex.Message}";

            }
            catch (Exception)
            {
                // Defensive: the StatusTextBlock may be null if the page is
                // not fully initialized yet, or the XAML surface is gone.
            }
        }
    }
}
