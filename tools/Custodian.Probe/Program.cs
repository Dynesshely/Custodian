using System.Diagnostics;
using Custodian.Core.Discovery;
using Custodian.Core.Launching;
using Custodian.Core.Sandboxie;

var showApps = args.Contains("--apps", StringComparer.OrdinalIgnoreCase);
var only = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));

var config = SandboxieConfig.Load();
Console.WriteLine($"INI      : {config.IniPath} (exists={File.Exists(config.IniPath)})");
Console.WriteLine($"FileRoot : {config.FileRootPathTemplate}");
Console.WriteLine($"Start.exe: {AppLauncher.StartExe ?? "<not found>"}");
Console.WriteLine($"Boxes    : {config.Boxes.Count}");
Console.WriteLine();

var reader = new WScriptShortcutReader();

foreach (var box in config.Boxes)
{
    if (only is not null && !box.Name.Contains(only, StringComparison.OrdinalIgnoreCase))
    {
        continue;
    }

    var sw = Stopwatch.StartNew();
    List<ShortcutCandidate> candidates;
    try
    {
        candidates = SandboxAppDiscoverer.EnumerateCandidates(box);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{box.Name}] 失败: {ex.Message}");
        continue;
    }

    var enumerateMs = sw.ElapsedMilliseconds;
    sw.Restart();

    var apps = reader is null
        ? new List<DiscoveredApp>()
        : SandboxAppDiscoverer.BuildApps(box, candidates, reader);

    sw.Stop();
    var group = box.GroupPath.Count == 0 ? "-" : string.Join(" / ", box.GroupPath);
    Console.WriteLine($"[{box.Name}] enabled={box.Enabled} group={group} exists={box.RootExists} candidates={candidates.Count} ({enumerateMs} ms) apps={apps.Count} ({sw.ElapsedMilliseconds} ms)");
    Console.WriteLine($"    root: {box.RootPath}");

    if (!showApps)
    {
        foreach (var candidate in candidates.OrderByDescending(c => c.Priority).ThenBy(c => c.Name).Take(12))
        {
            Console.WriteLine($"    {candidate.Priority,4} {candidate.Source,-12} {candidate.Name}");
        }

        continue;
    }

    foreach (var app in apps)
    {
        Console.WriteLine($"    {app.Priority,4} {app.Source,-10} {app.Name,-24} {app.TargetPath}");
        Console.WriteLine($"         -> {app.ResolvedTargetPath}");
    }
}

internal sealed class WScriptShortcutReader : IShortcutReader
{
    private readonly object? _shell;

    public WScriptShortcutReader()
    {
        var type = OperatingSystem.IsWindows() ? Type.GetTypeFromProgID("WScript.Shell") : null;
        _shell = type is null ? null : Activator.CreateInstance(type);
    }

    public ShortcutTarget? Read(string shortcutPath)
    {
        if (_shell is null)
        {
            return null;
        }

        try
        {
            dynamic shell = _shell;
            dynamic link = shell.CreateShortcut(shortcutPath);
            string target = link.TargetPath;
            string arguments = link.Arguments;
            string working = link.WorkingDirectory;
            return new ShortcutTarget(target, arguments ?? string.Empty, working ?? string.Empty);
        }
        catch
        {
            return null;
        }
    }
}
