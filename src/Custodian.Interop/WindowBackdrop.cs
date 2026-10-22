using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Custodian.Interop.Native;

namespace Custodian.Interop;

public enum BackdropKind
{
    None = 1,
    Mica = 2,
    Acrylic = 3,
    Tabbed = 4,
}

/// <summary>
/// DWM window material (Mica / Acrylic), immersive dark mode, rounded corners and frame extension.
/// All members are defensive: they never throw, they simply return false when the OS cannot comply.
/// </summary>
public static class WindowBackdrop
{
    /// <summary>Windows 11 22H2 (build 22621) introduced DWMWA_SYSTEMBACKDROP_TYPE.</summary>
    private const int MinimumBackdropBuild = 22621;

    public static bool IsSupported
    {
        get
        {
            try
            {
                return OperatingSystem.IsWindows() && Environment.OSVersion.Version.Build >= MinimumBackdropBuild;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Applies dark mode, rounded corners, extended frame and the requested DWM backdrop in one call.
    /// For WPF windows the composition surface is also made transparent so the material can show through.
    /// </summary>
    public static bool TryApply(IntPtr hwnd, BackdropKind kind = BackdropKind.Mica, bool dark = true)
    {
        try
        {
            if (hwnd == IntPtr.Zero || NativeMethods.IsWindow(hwnd) == 0)
            {
                return false;
            }

            PrepareWpfSurface(hwnd);
            SetDarkMode(hwnd, dark);
            SetRoundedCorners(hwnd, true);
            ExtendFrame(hwnd);

            if (!IsSupported)
            {
                return false;
            }

            return SetBackdrop(hwnd, kind);
        }
        catch
        {
            return false;
        }
    }

    public static void SetDarkMode(IntPtr hwnd, bool dark)
    {
        try
        {
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            var value = dark ? 1 : 0;
            if (NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int)) != 0)
            {
                NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE_LEGACY, ref value, sizeof(int));
            }
        }
        catch
        {
            // Attributes are best-effort.
        }
    }

    public static void SetRoundedCorners(IntPtr hwnd, bool rounded)
    {
        try
        {
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            var preference = rounded ? NativeMethods.DWMWCP_ROUND : NativeMethods.DWMWCP_DONOTROUND;
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
        }
        catch
        {
            // Ignored.
        }
    }

    public static void ExtendFrame(IntPtr hwnd)
    {
        try
        {
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            var margins = NativeMethods.MARGINS.FullWindow;
            NativeMethods.DwmExtendFrameIntoClientArea(hwnd, ref margins);
        }
        catch
        {
            // Ignored.
        }
    }

    private static bool SetBackdrop(IntPtr hwnd, BackdropKind kind)
    {
        try
        {
            var value = (int)kind;
            return NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, ref value, sizeof(int)) == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// WPF paints an opaque backing surface by default, which hides DWM materials.
    /// Making the composition target transparent (and the window brush empty) lets the material through.
    /// </summary>
    private static void PrepareWpfSurface(IntPtr hwnd)
    {
        try
        {
            var source = HwndSource.FromHwnd(hwnd);
            if (source is null)
            {
                return;
            }

            try
            {
                if (source.CompositionTarget is { } target)
                {
                    target.BackgroundColor = Colors.Transparent;
                }
            }
            catch
            {
                // Composition target may already be gone.
            }

            try
            {
                if (source.RootVisual is Window window)
                {
                    if (window.Background is null || window.Background is not SolidColorBrush { Color.A: 0 })
                    {
                        window.Background = Brushes.Transparent;
                    }
                }
            }
            catch
            {
                // Leaving the brush untouched is acceptable.
            }
        }
        catch
        {
            // Non-WPF hwnd (plain Win32 host): DWM attributes are still applied by the caller.
        }
    }
}
