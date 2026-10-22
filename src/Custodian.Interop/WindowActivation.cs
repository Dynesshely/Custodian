using Custodian.Interop.Native;

namespace Custodian.Interop;

/// <summary>
/// Brings a window to the foreground and gives it keyboard focus, even when it was minimized,
/// covered, or the call originates from a global hotkey (i.e. the foreground lock applies).
/// </summary>
public static class WindowActivation
{
    public static void ForceForeground(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            if (NativeMethods.IsWindow(hwnd) == 0)
            {
                return;
            }

            if (NativeMethods.IsIconic(hwnd) != 0)
            {
                NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
            }
            else
            {
                NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);
            }

            var foregroundWindow = NativeMethods.GetForegroundWindow();
            var foregroundThread = foregroundWindow == IntPtr.Zero
                ? 0u
                : NativeMethods.GetWindowThreadProcessId(foregroundWindow, out _);
            var windowThread = NativeMethods.GetWindowThreadProcessId(hwnd, out _);
            var currentThread = NativeMethods.GetCurrentThreadId();

            var attachedForeground = false;
            var attachedWindow = false;
            if (foregroundThread != 0 && foregroundThread != currentThread)
            {
                attachedForeground = NativeMethods.AttachThreadInput(currentThread, foregroundThread, 1) != 0;
            }

            if (windowThread != 0 && windowThread != currentThread && windowThread != foregroundThread)
            {
                attachedWindow = NativeMethods.AttachThreadInput(currentThread, windowThread, 1) != 0;
            }

            try
            {
                var previousTimeout = 0u;
                var timeoutRead = NativeMethods.SystemParametersInfoW(NativeMethods.SPI_GETFOREGROUNDLOCKTIMEOUT, 0, ref previousTimeout, 0) != 0;
                if (timeoutRead)
                {
                    var zero = 0u;
                    NativeMethods.SystemParametersInfoW(NativeMethods.SPI_SETFOREGROUNDLOCKTIMEOUT, 0, ref zero, NativeMethods.SPIF_SENDCHANGE);
                }

                try
                {
                    NativeMethods.BringWindowToTop(hwnd);
                    NativeMethods.SetForegroundWindow(hwnd);
                    NativeMethods.SetActiveWindow(hwnd);
                    NativeMethods.SetFocus(hwnd);
                }
                finally
                {
                    if (timeoutRead)
                    {
                        NativeMethods.SystemParametersInfoW(NativeMethods.SPI_SETFOREGROUNDLOCKTIMEOUT, 0, ref previousTimeout, NativeMethods.SPIF_SENDCHANGE);
                    }
                }
            }
            finally
            {
                if (attachedWindow)
                {
                    NativeMethods.AttachThreadInput(currentThread, windowThread, 0);
                }

                if (attachedForeground)
                {
                    NativeMethods.AttachThreadInput(currentThread, foregroundThread, 0);
                }
            }

            if (NativeMethods.GetForegroundWindow() != hwnd)
            {
                // Last resort: the topmost round-trip is the only reliable way past the foreground lock
                // when the process was not granted foreground rights.
                NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0, NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);
                NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_NOTOPMOST, 0, 0, 0, 0, NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE);
                NativeMethods.SetForegroundWindow(hwnd);
            }
        }
        catch
        {
            // Never propagate: activation failures must not break the caller.
        }
    }
}
