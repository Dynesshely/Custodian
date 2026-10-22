namespace Custodian.Core.Discovery;

/// <summary>沙盒内的一个快捷方式候选。</summary>
public sealed record ShortcutCandidate(string ShortcutPath, string Name, AppSourceKind Source, int Priority);

public sealed record ShortcutTarget(string TargetPath, string Arguments, string WorkingDirectory);

/// <summary>快捷方式解析抽象（实现由 WPF 侧基于 IShellLink 提供）。</summary>
public interface IShortcutReader
{
    ShortcutTarget? Read(string shortcutPath);
}

/// <summary>
/// 把沙盒目录里的 .lnk 分成「值得上面板的应用」与「噪音」。
/// 沙盒里塞满了卸载器、帮助文档、Office 最近文档、任务栏固定项等，必须过滤。
/// </summary>
public static class ShortcutClassifier
{
    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "$recycle.bin", "system volume information", "winsxs", "node_modules", ".git", ".svn",
        "recent", "jumplists", "jump lists", "iconcache", "temp", "tmp", "cache", "caches",
        "logs", "crashpad", "webcache", "inetcache", "gpucache", "dawncache", "service worker",
        "local storage", "blob storage", "shadercache", "firstrun", "config", "configs",
    };

    private static readonly string[] RejectedNameFragments =
    {
        "uninstall", "uninst", "卸载", "删除", "remove", "repair", "修复", "更新", "update",
        "upgrade", "setup", "install", "安装", "help", "帮助", "readme", "说明", "文档",
        "documentation", "license", "许可", "website", "官网", "forum", "论坛", "changelog",
        "release note", "whats new", "更多", "modify", "修改", "convert", "转换",
    };

    private static readonly HashSet<string> LaunchableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".bat", ".cmd", ".com",
    };

    public static bool ShouldSkipDirectory(string directoryName) => SkippedDirectories.Contains(directoryName);

    public static bool IsRejectedName(string name)
    {
        var lower = name.ToLowerInvariant();
        return RejectedNameFragments.Any(lower.Contains);
    }

    /// <summary>按快捷方式所处位置判断优先级：桌面 &gt; 开始菜单 &gt; 安装目录 &gt; 快速启动。</summary>
    public static (AppSourceKind Source, int Priority) Classify(string shortcutFullPath, string boxRootPath)
    {
        var relative = shortcutFullPath.Length > boxRootPath.Length
            ? shortcutFullPath[boxRootPath.Length..].ToLowerInvariant()
            : shortcutFullPath.ToLowerInvariant();

        if (relative.Contains("\\desktop\\", StringComparison.Ordinal))
        {
            return (AppSourceKind.Desktop, 100);
        }

        if (relative.Contains("\\start menu\\programs\\", StringComparison.Ordinal))
        {
            return (AppSourceKind.StartMenu, 90);
        }

        if (relative.Contains("\\quick launch\\", StringComparison.Ordinal) ||
            relative.Contains("\\user pinned\\", StringComparison.Ordinal))
        {
            return (AppSourceKind.QuickLaunch, 40);
        }

        if (relative.StartsWith("\\drive\\", StringComparison.Ordinal))
        {
            return (AppSourceKind.Other, 60);
        }

        if (relative.StartsWith("\\user\\", StringComparison.Ordinal))
        {
            return (AppSourceKind.Other, 50);
        }

        return (AppSourceKind.Other, 30);
    }

    public static bool IsLaunchableTarget(string? targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return false;
        }

        var extension = Path.GetExtension(targetPath);
        if (!LaunchableExtensions.Contains(extension))
        {
            return false;
        }

        var lower = targetPath.ToLowerInvariant();
        return !RejectedNameFragments.Any(lower.Contains);
    }
}
