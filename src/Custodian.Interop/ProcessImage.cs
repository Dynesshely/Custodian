using System.Runtime.InteropServices;
using System.Text;

namespace Custodian.Interop;

/// <summary>
/// 取进程镜像的「物理路径」。NtQueryInformationProcess(ProcessImageFileName) 返回的是
/// <c>\Device\HarddiskVolumeX\...</c> 这种卷路径 —— 对于沙盒里的进程，它指向沙盒内部真实文件，
/// 因为沙盒驱动在打开镜像时就重定向到了沙盒目录。这是判断进程属于哪个沙盒最准的依据。
/// </summary>
public static class ProcessImage
{
    private const int ProcessImageFileName = 27;
    private const uint ProcessQueryLimitedInformation = 0x1000;

    private static readonly Lazy<IReadOnlyDictionary<string, string>> DosDeviceMap = new(BuildDosDeviceMap, true);

    /// <summary>取进程镜像的物理路径（卷路径）；失败返回 null。</summary>
    public static string? TryGetPhysicalPath(int processId)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            const int BufferSize = 4096;
            var buffer = Marshal.AllocHGlobal(BufferSize);
            try
            {
                var status = NtQueryInformationProcess(handle, ProcessImageFileName, buffer, BufferSize, out _);
                if (status != 0)
                {
                    return null;
                }

                // UNICODE_STRING { ushort Length; ushort MaximumLength; IntPtr Buffer; }
                var length = Marshal.ReadInt16(buffer, 0);
                var pointer = Marshal.ReadIntPtr(buffer, IntPtr.Size);
                if (pointer == IntPtr.Zero || length <= 0)
                {
                    return null;
                }

                return Marshal.PtrToStringUni(pointer, length / 2);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>把 <c>\Device\HarddiskVolumeX\...</c> 换成盘符路径；映射不出来就原样返回。</summary>
    public static string ToDosPath(string ntPath)
    {
        if (ntPath.Length == 0 || !ntPath.StartsWith(@"\", StringComparison.Ordinal))
        {
            return ntPath;
        }

        var map = DosDeviceMap.Value;
        foreach (var (device, drive) in map)
        {
            if (!ntPath.StartsWith(device + @"\", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(ntPath, device, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return drive + ntPath[device.Length..];
        }

        return ntPath;
    }

    private static IReadOnlyDictionary<string, string> BuildDosDeviceMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var letter = 'A'; letter <= 'Z'; letter++)
        {
            var drive = letter + ":";
            var buffer = new StringBuilder(1024);
            try
            {
                if (QueryDosDevice(drive, buffer, buffer.Capacity) == 0)
                {
                    continue;
                }

                var device = buffer.ToString();
                if (device.Length > 0)
                {
                    map[device] = drive;
                }
            }
            catch
            {
                // 单个盘符查询失败不影响其它
            }
        }

        return map;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr process, int informationClass, IntPtr information, int size, out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int QueryDosDevice(string deviceName, StringBuilder targetPath, int max);
}
