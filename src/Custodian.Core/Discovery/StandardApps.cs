using Custodian.Core.Sandboxie;

namespace Custodian.Core.Discovery;

/// <summary>
/// 每个沙盒都能用的 Windows 自带工具。它们不依赖沙盒里的快捷方式，
/// 面板里统一折叠进「标准应用程序」分组，避免把每个沙盒的卡片都撑长。
/// </summary>
public static class StandardApps
{
    private static readonly (string Name, string Target)[] Catalog =
    {
        ("命令提示符", @"%SystemRoot%\System32\cmd.exe"),
        ("资源管理器", @"%SystemRoot%\explorer.exe"),
        ("Windows PowerShell", @"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"),
        ("注册表编辑器", @"%SystemRoot%\regedit.exe"),
        ("任务管理器", @"%SystemRoot%\System32\Taskmgr.exe"),
        ("控制面板", @"%SystemRoot%\System32\control.exe"),
        ("系统信息", @"%SystemRoot%\System32\msinfo32.exe"),
        ("磁盘清理", @"%SystemRoot%\System32\cleanmgr.exe"),
        ("事件查看器", @"%SystemRoot%\System32\eventvwr.exe"),
    };

    private static readonly string WindowsDirectory =
        Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    /// <summary>目标落在 Windows 目录下的，一律视为「标准应用程序」。</summary>
    public static bool IsStandardTarget(string? targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath) || WindowsDirectory.Length == 0)
        {
            return false;
        }

        if (!targetPath.StartsWith(WindowsDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return targetPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>生成该沙盒的标准应用清单（仅保留宿主上真实存在的 exe）。</summary>
    public static List<DiscoveredApp> Create(SandboxBox box)
    {
        var apps = new List<DiscoveredApp>(Catalog.Length);

        foreach (var (name, template) in Catalog)
        {
            var target = Environment.ExpandEnvironmentVariables(template);
            if (!File.Exists(target))
            {
                continue;
            }

            apps.Add(new DiscoveredApp(
                box.Name,
                name,
                target,
                string.Empty,
                Path.GetDirectoryName(target) ?? string.Empty,
                target,
                AppSourceKind.Other,
                0)
            {
                IsStandard = true,
            });
        }

        return apps;
    }
}
