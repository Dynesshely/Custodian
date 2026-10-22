using Custodian.Core.Sandboxie;

namespace Custodian.Core.Discovery;

/// <summary>
/// 宿主机路径 ↔ 沙盒内镜像路径。沙盒里安装的程序在宿主磁盘上不存在，
/// 但它的文件就在 <c>&lt;boxRoot&gt;\drive\C\...</c> 或 <c>&lt;boxRoot&gt;\user\current\...</c> 里。
/// 图标提取、文件探测都靠这个映射。
/// </summary>
public static class SandboxPathMapper
{
    /// <summary>
    /// 把沙盒里某个快捷方式的目标路径，解析成沙盒内部真实存在的文件。
    /// 返回 null 表示该目标只存在于宿主机（或根本不存在）——即这个应用不属于该沙盒。
    /// </summary>
    public static string? ResolveInBox(SandboxBox box, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length < 4)
        {
            return null;
        }

        var root = box.RootPath;
        if (!Directory.Exists(root))
        {
            return null;
        }

        // 快捷方式直接写了沙盒物理路径（有些安装器会把路径写死）
        if (IsUnder(path, root))
        {
            return File.Exists(path) ? path : null;
        }

        // 盘符路径：C:\Users\... -> <root>\drive\C\Users\...
        if (char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/'))
        {
            var drive = char.ToUpperInvariant(path[0]).ToString();
            var candidate = Probe(Path.Combine(root, "drive", drive, path[3..]));
            if (candidate is not null)
            {
                return candidate;
            }
        }

        // 用户目录 / ProgramData / Public 在沙盒里是独立镜像目录
        foreach (var (hostRoot, mirror) in MirrorRoots())
        {
            if (hostRoot.Length == 0 || !IsUnder(path, hostRoot))
            {
                continue;
            }

            var candidate = Probe(Path.Combine(root, mirror, path[hostRoot.Length..].TrimStart('\\', '/')));
            if (candidate is not null)
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>把宿主机绝对路径映射到沙盒内实际存在的文件；不存在则返回 null。</summary>
    public static string? MapToSandbox(SandboxBox box, string hostPath) => ResolveInBox(box, hostPath);

    /// <summary>物理路径是否就落在沙盒目录里（用于判断进程属于哪个沙盒）。</summary>
    public static bool IsInsideBox(SandboxBox box, string? path) =>
        !string.IsNullOrEmpty(path) && IsUnder(path, box.RootPath);

    private static string? Probe(string candidate) => File.Exists(candidate) ? candidate : null;

    /// <summary>前缀判断，且要求边界是路径分隔符（避免 C:\Users\a 命中 C:\Users\abc）。</summary>
    private static bool IsUnder(string path, string root)
    {
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return path.Length == root.Length || path[root.Length] is '\\' or '/';
    }

    private static IEnumerable<(string HostRoot, string Mirror)> MirrorRoots()
    {
        yield return (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Path.Combine("user", "current"));
        yield return (Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), Path.Combine("user", "all"));

        var publicRoot = Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments));
        if (!string.IsNullOrEmpty(publicRoot))
        {
            yield return (publicRoot, Path.Combine("user", "public"));
        }
    }
}
