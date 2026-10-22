namespace Custodian.Core.Discovery;

public enum AppSourceKind
{
    Other = 0,
    QuickLaunch = 1,
    StartMenu = 2,
    Desktop = 3,
}

/// <summary>沙盒里发现的一个可启动应用。</summary>
public sealed record DiscoveredApp(
    string SandboxName,
    string Name,
    string TargetPath,
    string Arguments,
    string WorkingDirectory,
    string ShortcutPath,
    AppSourceKind Source,
    int Priority)
{
    /// <summary>是否属于「标准应用程序」（Windows 自带工具）。</summary>
    public bool IsStandard { get; init; }

    /// <summary>
    /// 目标程序在沙盒内部的实际文件路径（<c>&lt;boxRoot&gt;\drive\C\...</c> 之类）。
    /// 只有它存在，才说明这个应用属于该沙盒；图标提取、运行态归属都用它。
    /// </summary>
    public string ResolvedTargetPath { get; init; } = string.Empty;

    /// <summary>取图标用的真实文件路径。</summary>
    public string IconPath => ResolvedTargetPath.Length > 0 ? ResolvedTargetPath : TargetPath;

    /// <summary>去重用的规范化键：目标路径 + 参数。</summary>
    public string Identity => (TargetPath + "|" + Arguments).ToLowerInvariant();

    /// <summary>
    /// 按沙盒内真实文件去重：同一个程序可能既有逻辑路径快捷方式、又有写死沙盒物理路径的快捷方式，
    /// 它们的 <see cref="TargetPath"/> 不同但实际指向同一个文件。
    /// </summary>
    public string ResolvedIdentity =>
        ((ResolvedTargetPath.Length > 0 ? ResolvedTargetPath : TargetPath) + "|" + Arguments).ToLowerInvariant();

    /// <summary>卡片上展示的路径（用环境变量缩写并保留可读性）。</summary>
    public string DisplayPath => FormatPath(TargetPath);

    public static string FormatPath(string path)
    {
        var replacements = new (string Root, string Token)[]
        {
            (Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "%ProgramFiles%"),
            (Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "%ProgramFiles(x86)%"),
            (Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "%LocalAppData%"),
            (Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "%AppData%"),
            (Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "%ProgramData%"),
            (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "%UserProfile%"),
        };

        foreach (var (root, token) in replacements)
        {
            if (root.Length > 0 && path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return token + path[root.Length..];
            }
        }

        return path;
    }
}
