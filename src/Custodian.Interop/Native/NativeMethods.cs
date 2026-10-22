using System.Runtime.InteropServices;
using System.Text;

namespace Custodian.Interop.Native;

/// <summary>
/// Central home for every P/Invoke, struct and constant used by the interop layer.
/// </summary>
internal static partial class NativeMethods
{
    // ---- window messages -------------------------------------------------
    internal const int WM_HOTKEY = 0x0312;

    // ---- DWM attributes --------------------------------------------------
    internal const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    internal const int DWMWA_USE_IMMERSIVE_DARK_MODE_LEGACY = 19;
    internal const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    internal const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    internal const int DWMWCP_DEFAULT = 0;
    internal const int DWMWCP_DONOTROUND = 1;
    internal const int DWMWCP_ROUND = 2;
    internal const int DWMWCP_ROUNDSMALL = 3;

    // ---- RegisterHotKey --------------------------------------------------
    internal const uint MOD_ALT = 0x0001;
    internal const uint MOD_CONTROL = 0x0002;
    internal const uint MOD_SHIFT = 0x0004;
    internal const uint MOD_WIN = 0x0008;
    internal const uint MOD_NOREPEAT = 0x4000;

    // ---- ShowWindow ------------------------------------------------------
    internal const int SW_HIDE = 0;
    internal const int SW_SHOW = 5;
    internal const int SW_RESTORE = 9;

    // ---- SetWindowPos ----------------------------------------------------
    internal static readonly IntPtr HWND_TOPMOST = new(-1);
    internal static readonly IntPtr HWND_NOTOPMOST = new(-2);

    internal const uint SWP_NOSIZE = 0x0001;
    internal const uint SWP_NOMOVE = 0x0002;
    internal const uint SWP_SHOWWINDOW = 0x0040;

    // ---- SystemParametersInfo -------------------------------------------
    internal const uint SPI_GETFOREGROUNDLOCKTIMEOUT = 0x2000;
    internal const uint SPI_SETFOREGROUNDLOCKTIMEOUT = 0x2001;
    internal const uint SPIF_SENDCHANGE = 0x0002;

    // ---- HwndSource / hidden window -------------------------------------
    internal static readonly IntPtr HWND_MESSAGE = new(-3);
    internal const int WS_EX_TOOLWINDOW = 0x00000080;
    internal const int WS_POPUP = unchecked((int)0x80000000);

    // ---- Shell item image factory ---------------------------------------
    internal const uint SIIGBF_RESIZETOFIT = 0x00000000;
    internal const uint SIIGBF_BIGGERSIZEOK = 0x00000001;
    internal const uint SIIGBF_ICONONLY = 0x00000004;
    internal const uint SIIGBF_THUMBNAILONLY = 0x00000008;

    internal static readonly Guid IID_IShellItemImageFactory = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

    // ---- SHGetFileInfo ---------------------------------------------------
    internal const uint SHGFI_ICON = 0x00000100;
    internal const uint SHGFI_LARGEICON = 0x00000000;
    internal const uint SHGFI_SMALLICON = 0x00000001;
    internal const uint SHGFI_SYSICONINDEX = 0x00004000;
    internal const uint SHGFI_USEFILEATTRIBUTES = 0x00000010;
    internal const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

    [StructLayout(LayoutKind.Sequential)]
    internal struct MARGINS
    {
        public int cxLeftWidth;
        public int cxRightWidth;
        public int cyTopHeight;
        public int cyBottomHeight;

        public static MARGINS FullWindow => new() { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SIZE
    {
        public int cx;
        public int cy;

        public SIZE(int cx, int cy)
        {
            this.cx = cx;
            this.cy = cy;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string? szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string? szTypeName;
    }

    // ---- dwmapi ----------------------------------------------------------
    [LibraryImport("dwmapi.dll")]
    internal static partial int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    [LibraryImport("dwmapi.dll")]
    internal static partial int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

    // ---- user32: window lifecycle / activation --------------------------
    [LibraryImport("user32.dll")]
    internal static partial int IsWindow(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    internal static partial int IsIconic(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    internal static partial int ShowWindow(IntPtr hwnd, int nCmdShow);

    [LibraryImport("user32.dll")]
    internal static partial int ShowWindowAsync(IntPtr hwnd, int nCmdShow);

    [LibraryImport("user32.dll")]
    internal static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    internal static partial int SetForegroundWindow(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    internal static partial int BringWindowToTop(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    internal static partial IntPtr SetActiveWindow(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    internal static partial IntPtr SetFocus(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    internal static partial uint GetWindowThreadProcessId(IntPtr hwnd, out uint lpdwProcessId);

    [LibraryImport("user32.dll")]
    internal static partial int AttachThreadInput(uint idAttach, uint idAttachTo, int fAttach);

    [LibraryImport("user32.dll")]
    internal static partial int GetWindowRect(IntPtr hwnd, out RECT lpRect);

    [LibraryImport("user32.dll")]
    internal static partial int SetWindowPos(IntPtr hwnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [LibraryImport("user32.dll")]
    internal static partial int SystemParametersInfoW(uint uiAction, uint uiParam, ref uint pvParam, uint fWinIni);

    [LibraryImport("user32.dll")]
    internal static partial int RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll")]
    internal static partial int UnregisterHotKey(IntPtr hWnd, int id);

    [LibraryImport("user32.dll")]
    internal static partial int PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    internal static partial int DestroyIcon(IntPtr hIcon);

    [LibraryImport("kernel32.dll")]
    internal static partial uint GetCurrentThreadId();

    // ---- gdi32 -----------------------------------------------------------
    [LibraryImport("gdi32.dll")]
    internal static partial int DeleteObject(IntPtr hObject);

    // ---- shell32 ---------------------------------------------------------
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern int SHDefExtractIcon(string pszIconFile, int iIndex, uint uFlags, out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIconSize);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern int SHGetPathFromIDList(IntPtr pidl, StringBuilder pszPath);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    internal static extern void SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);
}
