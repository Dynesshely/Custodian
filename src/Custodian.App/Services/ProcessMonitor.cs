using System.Diagnostics;
using System.IO;
using Custodian.Core.Discovery;
using Custodian.Core.Sandboxie;
using Custodian.Interop;

namespace Custodian.App.Services;

/// <summary>进程采样结果。</summary>
public sealed class RunningSnapshot
{
    /// <summary>进程名 → 存活中的 pid（只包含面板关心的名字）。</summary>
    public Dictionary<string, HashSet<int>> PidsByName { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>沙盒名 → 正在该沙盒内运行的进程名。</summary>
    public Dictionary<string, HashSet<string>> NamesByBox { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// 定时采样进程，判断「哪些程序正在哪个沙盒里运行」。
///
/// 沙盒里的进程上报的镜像路径是逻辑路径（<c>C:\Program Files\...</c>），
/// 但 <see cref="ProcessImage.TryGetPhysicalPath"/> 能拿到它真实的卷路径
/// （<c>\Device\HarddiskVolumeX\Sandbox\...\box\drive\C\...</c>），
/// 落在哪个沙盒目录下就属于哪个沙盒。宿主机上同名程序（宿主正在跑的 Brave、Docker、
/// 甚至是同一个程序在宿主上的另一份安装）都不会被算进沙盒。
/// </summary>
public sealed class ProcessMonitor : IDisposable
{
    private readonly TimeSpan _interval;
    private readonly Func<RunningSnapshot>? _sample;
    private Timer? _timer;
    private volatile bool _disposed;
    private volatile bool _active = true;

    public ProcessMonitor(Func<RunningSnapshot>? sample = null, TimeSpan? interval = null)
    {
        _sample = sample;

        // 5 秒足够表达「运行中」状态；一次采样要枚举全系统进程（约 20ms），
        // 2.5 秒间隔等于常驻 1% 单核，不值。
        _interval = interval ?? TimeSpan.FromSeconds(5);
    }

    /// <summary>面板隐藏到托盘时暂停采样（唤醒时立刻补一次）。</summary>
    public void SetActive(bool active)
    {
        _active = active;
        if (active)
        {
            RefreshNow();
        }
    }

    public event EventHandler<RunningSnapshot>? SnapshotUpdated;

    /// <summary>累计采样次数（性能诊断用）。</summary>
    public static long CaptureCount { get; private set; }

    /// <summary>最近一次采样耗时（毫秒）。</summary>
    public static double LastCaptureMs { get; private set; }

    /// <summary>枚举一遍进程（只解析名字命中的那些，宿主上几百个进程不会拖慢）。</summary>
    public static RunningSnapshot Capture(IReadOnlyList<SandboxBox> boxes, IReadOnlyCollection<string> interestingNames)
    {
        var snapshot = new RunningSnapshot();
        if (boxes.Count == 0 || interestingNames.Count == 0)
        {
            return snapshot;
        }

        var interesting = new HashSet<string>(interestingNames, StringComparer.OrdinalIgnoreCase);

        // 先用 NtQuerySystemInformation 一次拿到全系统 pid + 映像名（比逐个 Process 对象便宜），
        // 只有名字命中的进程才需要开句柄查物理路径。
        var entries = SystemProcesses.Snapshot();
        if (entries.Count == 0)
        {
            return snapshot;
        }

        foreach (var entry in entries)
        {
            {
                var name = Path.GetFileNameWithoutExtension(entry.Name);
                if (name.Length == 0 || !interesting.Contains(name))
                {
                    continue;
                }

                if (!snapshot.PidsByName.TryGetValue(name, out var pids))
                {
                    pids = new HashSet<int>();
                    snapshot.PidsByName[name] = pids;
                }

                pids.Add(entry.Pid);

                var boxName = LocateBox(boxes, entry.Pid);
                if (boxName is null)
                {
                    continue;
                }

                if (!snapshot.NamesByBox.TryGetValue(boxName, out var names))
                {
                    names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    snapshot.NamesByBox[boxName] = names;
                }

                names.Add(name);
            }
        }

        return snapshot;
    }

    /// <summary>进程属于哪个沙盒；宿主机进程返回 null。</summary>
    private static string? LocateBox(IReadOnlyList<SandboxBox> boxes, int processId)
    {
        var physical = ProcessImage.TryGetPhysicalPath(processId);
        if (physical is not null)
        {
            var dosPath = ProcessImage.ToDosPath(physical);
            if (!IsVolumePath(dosPath))
            {
                // 路径已经定位到具体卷：落在哪个沙盒目录里就是哪个沙盒，否则就是宿主机进程
                foreach (var box in boxes)
                {
                    if (SandboxPathMapper.IsInsideBox(box, dosPath))
                    {
                        return box.Name;
                    }
                }

                return null;
            }
        }

        // 拿不到卷路径（少见：跨会话、被保护的进程）时退回逻辑路径映射，
        // 只有当镜像只在唯一一个沙盒里存在时才认领，避免把宿主机程序算进沙盒。
        string? logical = null;
        try
        {
            using var process = Process.GetProcessById(processId);
            logical = process.MainModule?.FileName;
        }
        catch
        {
            logical = null;
        }

        if (string.IsNullOrEmpty(logical))
        {
            return null;
        }

        string? match = null;
        foreach (var box in boxes)
        {
            if (SandboxPathMapper.ResolveInBox(box, logical) is null)
            {
                continue;
            }

            if (match is not null)
            {
                return null;
            }

            match = box.Name;
        }

        return match;
    }

    private static bool IsVolumePath(string path) =>
        path.StartsWith(@"\??\", StringComparison.Ordinal) ||
        path.StartsWith(@"\Device\", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(@"\GLOBAL??\", StringComparison.OrdinalIgnoreCase);

    public void Start() => _timer ??= new Timer(_ => Pump(), null, TimeSpan.FromSeconds(1), _interval);

    public void RefreshNow() => Pump();

    private void Pump()
    {
        if (_disposed || _sample is null || !_active)
        {
            return;
        }

        RunningSnapshot snapshot;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            snapshot = _sample();
        }
        catch
        {
            return;
        }
        stopwatch.Stop();

        CaptureCount++;
        LastCaptureMs = stopwatch.Elapsed.TotalMilliseconds;

        if (!_disposed)
        {
            SnapshotUpdated?.Invoke(this, snapshot);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _timer?.Dispose();
        _timer = null;
    }
}
