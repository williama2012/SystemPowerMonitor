using System.Runtime.InteropServices;

namespace SysPwrMon.Native
{
    /// <summary>
    /// Win32 window-icon interop. An unpackaged WinUI 3 app gets no running
    /// taskbar/title-bar icon from the .exe file icon (ApplicationIcon) or
    /// Package.appxmanifest (ignored once MSIX tooling is off), so Windows
    /// shows the default. The icon must be set on the HWND explicitly. The
    /// resolved WindowsAppSDK (2.6.5) exposes no managed AppWindow icon API,
    /// so this uses the Win32 path every desktop process supports.
    /// Trade-off: a small P/Invoke layer (not a managed API) for a
    /// version-stable, deterministic result — chosen over a WindowsAppSDK bump.
    /// </summary>
    internal static class WindowIcon
    {
        /// <summary>
        /// Load <paramref name="iconPath"/> (.ico) and set it as the window's
        /// small (title bar) and big (taskbar / Alt-Tab) icon. Fails soft: a
        /// missing or undecodable .ico leaves the default icon in place — the
        /// icon is cosmetic and must never block launch.
        /// </summary>
        /// <param name="hwnd">The window handle to decorate.</param>
        /// <param name="iconPath">Fully-qualified path to the .ico file.</param>
        public static void SetWindowIcon(nint hwnd, string iconPath)
        {
            // cx=cy=0 → load at the image's native size: for a multi-size .ico
            // that is the largest stored image, so Windows has a high-res
            // source to scale into the 16px and 32px slots.
            nint hIcon = LoadImage(nint.Zero, iconPath, ImageType.ImageIcon, 0, 0, LoadImageFlags.LoadFromFile);
            if (hIcon == nint.Zero)
                return; // Fail soft: keep the default icon rather than break launch.

            // Class icons: the title bar (GCLP_HICON, small) and the
            // Alt-Tab preview (GCLP_HICONBIG, big).
            SetClassLongPtr(hwnd, GclpHicon, hIcon);
            SetClassLongPtr(hwnd, GclpHiconBig, hIcon);

            // The taskbar BUTTON reads the per-window icon set via WM_SETICON,
            // not the class icon — it is the one slot that does not pick up
            // GCLP, so notify the shell with the small and big window icons.
            SendMessageW(hwnd, WmSetIcon, (nint)IconSmall, hIcon);
            SendMessageW(hwnd, WmSetIcon, (nint)IconBig, hIcon);
        }

        // WM_SETICON — 0x0080; its wParam selects the icon slot.
        private const uint WmSetIcon = 0x0080;

        // WM_SETICON wParam values — ICON_SMALL / ICON_BIG.
        private const int IconSmall = 0;
        private const int IconBig = 1;

        // GCLP icon indices — the negative long-form indices SetClassLongPtr
        // expects for the small/big window icons (not magic: GCLP_HICON=-0x400,
        // GCLP_HICONBIG=-0x401 per the Win32 window-class long indices).
        private const int GclpHicon = -0x400;
        private const int GclpHiconBig = -0x401;

        // Loads the icon from the file named in <c>name</c>; returns HICON.
        [DllImport("user32.dll", EntryPoint = "LoadImageW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern nint LoadImage(nint hInstance, string name, ImageType type, int cx, int cy, LoadImageFlags flags);

        // Sets a window-class "long" value (here, an icon handle) on the HWND.
        [DllImport("user32.dll", EntryPoint = "SetClassLongPtr", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern nint SetClassLongPtr(nint hWnd, int nIndex, nint newValue);

        // Delivers WM_SETICON so the taskbar button adopts the window icon.
        [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
        private static extern nint SendMessageW(nint hWnd, uint msg, nint wParam, nint lParam);

        // IMAGE_ICON — the resource type of .ico/.exe icon images.
        private enum ImageType : uint { ImageIcon = 1 }

        // LR_LOADFROMFILE — load from the file named in <c>name</c>.
        [Flags]
        private enum LoadImageFlags : uint { LoadFromFile = 0x10 }
    }
}
