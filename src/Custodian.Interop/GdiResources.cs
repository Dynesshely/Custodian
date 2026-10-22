using System.Runtime.InteropServices;

namespace Custodian.Interop;

/// <summary>取进程的 GDI / USER 对象数（WPF 界面泄漏排查常用指标）。</summary>
public static class GdiResources
{
    private const uint GdiObjects = 0;
    private const uint UserObjects = 1;

    public static int GetGdiObjectCount(int processId)
    {
        using var process = System.Diagnostics.Process.GetProcessById(processId);
        return (int)GetGuiResources(process.Handle, GdiObjects);
    }

    public static int GetUserObjectCount(int processId)
    {
        using var process = System.Diagnostics.Process.GetProcessById(processId);
        return (int)GetGuiResources(process.Handle, UserObjects);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetGuiResources(IntPtr process, uint flags);
}
