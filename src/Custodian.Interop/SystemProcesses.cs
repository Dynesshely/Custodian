using System.Runtime.InteropServices;

namespace Custodian.Interop;

/// <summary>进程快照里的一项：pid + 映像名（不含路径，带扩展名）。</summary>
public readonly record struct ProcessEntry(int Pid, string Name);

/// <summary>
/// 一次性拿到全系统进程列表（pid + 映像名）。
/// 用 <c>NtQuerySystemInformation(SystemProcessInformation)</c>：一次调用返回所有进程，
/// 比 <c>Process.GetProcesses()</c>（每个进程都要建一个 Process 对象）快一个数量级 ——
/// 这是常驻进程监控（每 2.5 秒采样一次）的主要开销来源。
/// </summary>
public static class SystemProcesses
{
    private const int SystemProcessInformation = 5;
    private const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);

    private static readonly object Gate = new();
    private static IntPtr _buffer = IntPtr.Zero;
    private static int _bufferSize;

    /// <summary>取全系统进程快照；失败时返回空列表（调用方自行回退）。</summary>
    public static List<ProcessEntry> Snapshot()
    {
        lock (Gate)
        {
            try
            {
                for (var attempt = 0; attempt < 4; attempt++)
                {
                    var size = _bufferSize > 0 ? _bufferSize : 1536 * 1024;
                    if (_buffer == IntPtr.Zero || size > _bufferSize)
                    {
                        if (_buffer != IntPtr.Zero)
                        {
                            Marshal.FreeHGlobal(_buffer);
                        }

                        _buffer = Marshal.AllocHGlobal(size);
                        _bufferSize = size;
                    }

                    var status = NtQuerySystemInformation(SystemProcessInformation, _buffer, _bufferSize, out var needed);
                    if (status == StatusInfoLengthMismatch)
                    {
                        var bigger = Math.Max(_bufferSize * 2, needed + 64 * 1024);
                        Marshal.FreeHGlobal(_buffer);
                        _buffer = Marshal.AllocHGlobal(bigger);
                        _bufferSize = bigger;
                        continue;
                    }

                    if (status != 0)
                    {
                        return new List<ProcessEntry>();
                    }

                    return Parse(_buffer, _bufferSize);
                }

                return new List<ProcessEntry>();
            }
            finally
            {
                // 缓冲区常驻复用，不在这里释放
            }
        }
    }

    private static List<ProcessEntry> Parse(IntPtr buffer, int size)
    {
        var result = new List<ProcessEntry>(512);
        var offset = 0;

        while (true)
        {
            var entry = buffer + offset;
            var next = Marshal.ReadInt32(entry);           // NextEntryOffset

            // x64 布局：ImageName(UNICODE_STRING) 在 56，UniqueProcessId 在 80
            var pid = (int)(long)Marshal.ReadIntPtr(entry, 80);
            var name = ReadImageName(entry);

            if (pid > 0 && name.Length > 0)
            {
                result.Add(new ProcessEntry(pid, name));
            }

            if (next <= 0 || offset + next + 8 >= size)
            {
                break;
            }

            offset += next;
        }

        return result;
    }

    private static string ReadImageName(IntPtr entry)
    {
        var length = (ushort)Marshal.ReadInt16(entry, 56);
        if (length == 0 || length > 1024)
        {
            return string.Empty;
        }

        var buffer = Marshal.ReadIntPtr(entry, 64);
        if (buffer == IntPtr.Zero)
        {
            return string.Empty;
        }

        return Marshal.PtrToStringUni(buffer, length / 2) ?? string.Empty;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(int infoClass, IntPtr info, int length, out int returnLength);
}
