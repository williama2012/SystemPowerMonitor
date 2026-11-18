using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SysPwrMon.Services;

namespace SysPwrMon.Pages;

// Thin view over ISettingsStore + TelemetryService: the registry is the
// source of truth, so values are re-read on every visit (they may have
// changed externally, e.g. in regedit). Save = store.Save + push into
// the running sink; both steps are deliberate and visible.
public sealed partial class SettingsPage : Page
{
    private readonly TelemetryService _telemetry;
    private readonly ISettingsStore _store;

    public SettingsPage()
    {
        InitializeComponent();

        var app = Application.Current;
        if (app is not App sysPwrMonApp)
            throw new InvalidOperationException("Expected the SysPwrMon application instance.");

        _telemetry = sysPwrMonApp.Telemetry;
        _store = sysPwrMonApp.SettingsStore;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        var settings = _store.Load();
        MongoUriTextBox.Text = settings.MongoUri;
        DatabaseNameTextBox.Text = settings.DatabaseName;
        CollectionNameTextBox.Text = settings.CollectionName;
        MinimizeToTrayCheckBox.IsChecked = settings.MinimizeToTray;
        SaveStatusTextBlock.Text = string.Empty;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        // Load-modify-save: the page only owns the Mongo fields + the tray
        // toggle; WindowBounds lives in the same record and must survive a
        // settings save (a fresh record would null it and the store would
        // delete the value).
        var settings = _store.Load() with
        {
            MongoUri = MongoUriTextBox.Text.Trim(),
            DatabaseName = DatabaseNameTextBox.Text.Trim(),
            CollectionName = CollectionNameTextBox.Text.Trim(),
            MinimizeToTray = MinimizeToTrayCheckBox.IsChecked == true
        };

        if (!settings.IsComplete)
        {
            SaveStatusTextBlock.Text = "All three values are required.";
            return;
        }

        _store.Save(settings);
        _telemetry.ApplySettings(settings);
        SaveStatusTextBlock.Text = "Saved to Windows registry.";
    }
}
