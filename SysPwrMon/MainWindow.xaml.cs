using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using SysPwrMon.Pages;
using SysPwrMon.Services;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Input;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace SysPwrMon
{
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // Tray wiring (PingApp parity): left-click restores, right-click opens
            // the Show/Exit menu. H.NotifyIcon's ContextFlyout builds a Win32
            // PopupMenu from the MenuFlyout and invokes each item's Command —
            // so the items carry RelayCommand, not Click handlers.
            //
            // Icon is set from an absolute path (not the XAML string): this app is
            // unpackaged, so the string form is resolved by H.NotifyIcon's own
            // converter against an uncertain base; an explicit BitmapImage from
            // AppContext.BaseDirectory is the same .ico the window icon uses and
            // can't dangle.
            TrayIcon.IconSource = new BitmapImage(
                new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico")));
            TrayIcon.LeftClickCommand = new RelayCommand(RestoreFromTray);
            TrayShowItem.Command = new RelayCommand(RestoreFromTray);
            TrayExitItem.Command = new RelayCommand(ExitFromTray);

            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
            AppWindow.SetIcon("Assets/AppIcon.ico");
            Activated += OnActivated;
            AppWindow.Closing += OnAppWindowClosing;
        }

        // The requirement: no UI updates while minimized, sampling/logging
        // continue. WinUI 3 removed the managed minimize state (the
        // WindowActivationState enum only carries CodeActivated /
        // PointerActivated / Deactivated), so the window is still the only
        // component that knows its own state — here it reports suspension to
        // the service. Trade-off: we read the native show-command on each
        // activation rather than polling; a window that starts minimized
        // reports on first activation.
        private void OnActivated(object? sender, WindowActivatedEventArgs args)
        {
            if (Application.Current is not App app)
                return;

            // Minimize-to-tray: when the setting is on, a minimize tucks the
            // window entirely (SW_HIDE — no taskbar button) and shows the tray.
            // Suspension then covers "minimized" OR "tucked"; the tray's own
            // visibility is driven by the hide/restore path below, not here, so
            // a re-activation after SW_HIDE (showCmd resets to SW_HIDE) can't
            // clobber the visible tray.
            if (IsMinimized() && app.MinimizeToTrayEnabled)
                HideToTray();

            app.SetUiSuspended(IsMinimized() || _hiddenToTray);
        }

        // Persist the window's bounds on a real close. The tray tuck path
        // (HideToTray, SW_HIDE) is NOT a close, so it never fires here — a
        // tucked window keeps its spot, and only the close path persists.
        private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            if (Application.Current is App app)
                app.SaveWindowBounds(AppWindow.Position, AppWindow.Size);
        }

        // Tuck into the tray: hide the HWND (removes the taskbar button) and
        // show the tray icon. After SW_HIDE the window is hidden, not
        // minimized (showCmd is SW_HIDE=0, not the minimized value), so
        // _hiddenToTray tracks the tucked state separately.
        private void HideToTray()
        {
            ShowWindow(Handle, SwHide);
            _hiddenToTray = true;
        }

        // Restore from the tray: bring the window back normal, drop the tucked
        // state, and take focus. Used by both the left-click command and the
        // menu "Show". The tray icon stays — always present (the restore entry);
        // only the window shows/hides.
        private void RestoreFromTray()
        {
            ShowWindow(Handle, SwRestore);
            _hiddenToTray = false;
            SetForegroundWindow(Handle);
            Activate();
        }

        // Menu "Exit": close the window; the app exits with it (single window).
        private void ExitFromTray() => Close();

        // Tucked state, distinct from minimized (see HideToTray).
        private bool _hiddenToTray;

        // The HWND: for Win32-backed WinUI 3 windows, AppWindow.Id.Value is the
        // HWND (the same shortcut IsMinimized relies on).
        private IntPtr Handle => new IntPtr((long)AppWindow.Id.Value);

        // SW_HIDE / SW_RESTORE — the show-window commands (user32).
        private const int SwHide = 0;
        private const int SwRestore = 9;

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private bool IsMinimized()
        {
            // For Win32-backed WinUI 3 windows, AppWindow.Id.Value is the HWND;
            // this is the documented (if implementation-tied) shortcut. DO NOT
            // treat the value as an opaque id if we ever support non-Win32
            // window types — re-derive the handle then.
            var placement = new WindowPlacement { length = Marshal.SizeOf<WindowPlacement>() };
            return GetWindowPlacement(new IntPtr((long)AppWindow.Id.Value), ref placement) &&
                placement.showCmd == ShowMinimized;
        }

        // SWP_SHOWMINIMIZED from user32.
        private const int ShowMinimized = 2;

        // Full sequential layout: GetWindowPlacement writes the whole struct.
        [StructLayout(LayoutKind.Sequential)]
        private struct WindowPlacement
        {
            public int length;
            public int flags;
            public int showCmd;
            public int minPositionX;
            public int minPositionY;
            public int maxPositionX;
            public int maxPositionY;
            public int normalPositionX;
            public int normalPositionY;
            public int normalSizeX;
            public int normalSizeY;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowPlacement(IntPtr hWnd, ref WindowPlacement lpwndplace);

        private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
        {
            NavView.IsPaneOpen = !NavView.IsPaneOpen;
        }

        private void TitleBar_BackRequested(TitleBar sender, object args)
        {
            NavFrame.GoBack();
        }

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.IsSettingsSelected)
            {
                NavFrame.Navigate(typeof(SettingsPage));
            }
            else if (args.SelectedItem is NavigationViewItem item)
            {
                switch (item.Tag)
                {
                    case "home":
                        NavFrame.Navigate(typeof(HomePage));
                        break;
                    case "about":
                        NavFrame.Navigate(typeof(AboutPage));
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown navigation item tag: {item.Tag}");
                }
            }
        }
    }
}
