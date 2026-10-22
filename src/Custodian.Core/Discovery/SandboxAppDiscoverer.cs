namespace Custodian.Core.Discovery;

using Custodian.Core.Sandboxie;

/// <summary>遍历沙盒目录，找出可展示的应用。</summary>
public sealed class SandboxAppDiscoverer
{
    private const int MaxDepth = 14;
    private const int MaxCandidatesPerBox = 4000;

    /// <summary>枚举沙盒里所有 .lnk 候选（已按名字过滤噪音）。不做 I/O 之外的昂贵操作。</summary>
    public static List<ShortcutCandidate> EnumerateCandidates(SandboxBox box, CancellationToken cancellationToken = default)
    {
        var results = new List<ShortcutCandidate>();
        var root = box.RootPath;

        if (!Directory.Exists(root))
        {
            return results;
        }

        var stack = new Stack<(string Path, int Depth)>();
        stack.Push((root, 0));

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (current, depth) = stack.Pop();

            if (depth > MaxDepth)
            {
                continue;
            }

            IEnumerable<FileSystemInfo> entries;
            try
            {
                entries = new DirectoryInfo(current).EnumerateFileSystemInfos();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or DirectoryNotFoundException)
            {
                continue;
            }

            try
            {
                foreach (var entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (entry is DirectoryInfo directory)
                    {
                        if (directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
                        {
                            continue;
                        }

                        if (ShortcutClassifier.ShouldSkipDirectory(directory.Name))
                        {
                            continue;
                        }

                        stack.Push((directory.FullName, depth + 1));
                        continue;
                    }

                    if (!entry.Extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var name = Path.GetFileNameWithoutExtension(entry.Name);
                    if (name.Length == 0 || ShortcutClassifier.IsRejectedName(name))
                    {
                        continue;
                    }

                    var (source, priority) = ShortcutClassifier.Classify(entry.FullName, root);
                    results.Add(new ShortcutCandidate(entry.FullName, name, source, priority));

                    if (results.Count >= MaxCandidatesPerBox)
                    {
                        return results;
                    }
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                // 单个目录不可读不影响整体
            }
        }

        return results;
    }

    /// <summary>
    /// 把候选解析为目标程序、过滤无效项并按目标去重（桌面优先）。
    /// 只有目标真实存在于沙盒内部的快捷方式才算这个沙盒的应用：
    /// 沙盒目录里混着大量从宿主机写时复制进来的快捷方式（开始菜单、任务栏固定项），
    /// 它们指向宿主机上的程序，运行和安装都与本沙盒无关。
    /// </summary>
    public static List<DiscoveredApp> BuildApps(
        SandboxBox box,
        IEnumerable<ShortcutCandidate> candidates,
        IShortcutReader reader,
        CancellationToken cancellationToken = default)
    {
        var best = new Dictionary<string, DiscoveredApp>(StringComparer.Ordinal);

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ShortcutTarget? target;
            try
            {
                target = reader.Read(candidate.ShortcutPath);
            }
            catch
            {
                target = null;
            }

            if (target is null || !ShortcutClassifier.IsLaunchableTarget(target.TargetPath))
            {
                continue;
            }

            // 目标必须落在沙盒内部，否则就是宿主机应用的快捷方式副本
            var resolved = SandboxPathMapper.ResolveInBox(box, target.TargetPath);
            if (resolved is null)
            {
                continue;
            }

            var app = new DiscoveredApp(
                box.Name,
                candidate.Name,
                target.TargetPath,
                target.Arguments,
                target.WorkingDirectory,
                candidate.ShortcutPath,
                candidate.Source,
                candidate.Priority)
            {
                ResolvedTargetPath = resolved,
            };

            if (!best.TryGetValue(app.ResolvedIdentity, out var existing) || IsBetter(app, existing, box))
            {
                best[app.ResolvedIdentity] = app;
            }
        }

        // 二级去重：同一个沙盒里两张卡都叫「飞书」的，只留更可信的那一张
        var byName = new Dictionary<string, DiscoveredApp>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in best.Values.OrderByDescending(a => a.Priority).ThenBy(a => a.ShortcutPath.Length))
        {
            byName.TryAdd(app.Name, app);
        }

        return byName.Values
            .OrderByDescending(a => a.Priority)
            .ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 同一目标出现多条快捷方式时挑一条：优先级高的优先；
    /// 优先级相同时优先保留逻辑路径（<c>C:\Program Files\...</c>）而不是写死的沙盒物理路径
    /// （后者会把 <c>D:\Sandbox\...</c> 直接暴露在卡片上），再按快捷方式路径短的。
    /// </summary>
    private static bool IsBetter(DiscoveredApp candidate, DiscoveredApp existing, SandboxBox box)
    {
        if (candidate.Priority != existing.Priority)
        {
            return candidate.Priority > existing.Priority;
        }

        var candidatePhysical = IsPhysicalPath(candidate.TargetPath, box);
        var existingPhysical = IsPhysicalPath(existing.TargetPath, box);
        if (candidatePhysical != existingPhysical)
        {
            return !candidatePhysical;
        }

        return candidate.ShortcutPath.Length < existing.ShortcutPath.Length;
    }

    private static bool IsPhysicalPath(string path, SandboxBox box) =>
        path.StartsWith(box.RootPath, StringComparison.OrdinalIgnoreCase);
}
