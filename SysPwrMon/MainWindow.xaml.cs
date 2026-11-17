using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SysPwrMon.Pages;
using System.Runtime.InteropServices;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace SysPwrMon
{
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
            AppWindow.SetIcon("Assets/AppIcon.ico");
            Activated += OnActivated;
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
            app.SetUiSuspended(IsMinimized());
        }

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
