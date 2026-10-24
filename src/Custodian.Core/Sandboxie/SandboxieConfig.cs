using System.Security.Principal;
using Custodian.Core.Ini;

namespace Custodian.Core.Sandboxie;

/// <summary>读取 Sandboxie.ini，推导沙盒列表与根目录。</summary>
public sealed class SandboxieConfig
{
    private const string DefaultFileRootPath = @"C:\Sandbox\%USER%\%SANDBOX%";

    private static readonly string[] NonBoxSections =
    {
        "GlobalSettings",
        "UserSettings",
        "Template",
    };

    private readonly IniDocument _document;

    private SandboxieConfig(
        string iniPath,
        IniDocument document,
        string fileRootPathTemplate,
        string rootPathSource,
        IReadOnlyList<SandboxBox> boxes)
    {
        IniPath = iniPath;
        _document = document;
        FileRootPathTemplate = fileRootPathTemplate;
        RootPathSource = rootPathSource;
        Boxes = boxes;
    }

    public string IniPath { get; }

    /// <summary>实际生效的 FileRootPath 模板（可能是自定义值）。</summary>
    public string FileRootPathTemplate { get; }

    /// <summary>根目录来源：<c>custom</c>（设置里覆盖）/ <c>ini</c>（Sandboxie.ini）/ <c>default</c>（内置默认）。</summary>
    public string RootPathSource { get; }

    public bool IsRootOverridden => RootPathSource == "custom";

    public IReadOnlyList<SandboxBox> Boxes { get; }

    public static string DefaultIniPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Sandboxie.ini");

    /// <param name="iniPath">Sandboxie.ini 路径；为空时用系统默认位置。</param>
    /// <param name="rootPathOverride">设置里的「自定义沙盒存储位置」；为空则跟随 Sandboxie.ini。</param>
    public static SandboxieConfig Load(string? iniPath = null, string? rootPathOverride = null)
    {
        var path = iniPath ?? DefaultIniPath;
        var document = File.Exists(path) ? IniDocument.Load(path) : IniDocument.Parse(string.Empty);
        var global = document.Find("GlobalSettings");

        var source = "ini";
        var fileRootPath = rootPathOverride?.Trim();
        if (string.IsNullOrEmpty(fileRootPath))
        {
            fileRootPath = global?.GetValue("FileRootPath");
            if (string.IsNullOrWhiteSpace(fileRootPath))
            {
                fileRootPath = DefaultFileRootPath;
                source = "default";
            }
        }
        else
        {
            source = "custom";
        }

        var groupTree = BuildGroupTree(document);
        var order = FlattenGroupTree(groupTree);

        var boxes = new List<SandboxBox>();
        foreach (var section in document.Sections)
        {
            if (IsNonBoxSection(section.Name))
            {
                continue;
            }

            var template = section.GetValue("FileRootPath");
            if (string.IsNullOrWhiteSpace(template))
            {
                template = fileRootPath;
            }

            boxes.Add(new SandboxBox
            {
                Name = section.Name,
                Enabled = section.GetBool("Enabled", true),
                FileRootPathTemplate = section.GetValue("FileRootPath"),
                RootPath = ExpandPath(template, section.Name),
                Templates = section.GetValues("Template").ToArray(),
                GroupPath = order.TryGetValue(section.Name, out var groups) ? groups : Array.Empty<string>(),
            });
        }

        // 按分组顺序排列；未出现在 BoxGrouping 里的按名字排在后面
        var rank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        foreach (var name in FlattenOrder(groupTree, document))
        {
            if (!rank.ContainsKey(name))
            {
                rank[name] = index++;
            }
        }

        var sorted = boxes
            .OrderBy(b => rank.TryGetValue(b.Name, out var r) ? r : int.MaxValue)
            .ThenBy(b => b.GroupPath.Count)
            .ThenBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // 磁盘上存在但 ini 里没有登记的沙盒目录，也当成 box 展示（标记为 enabled）
        var known = sorted.Select(b => b.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var inferred in DiscoverUnregisteredBoxes(fileRootPath, known))
        {
            sorted.Add(inferred);
        }

        return new SandboxieConfig(path, document, fileRootPath, source, sorted);
    }

    public static string ExpandPath(string template, string boxName)
    {
        var expanded = template;

        expanded = expanded
            .Replace("%SANDBOX%", boxName, StringComparison.OrdinalIgnoreCase)
            .Replace("%USER%", Environment.UserName, StringComparison.OrdinalIgnoreCase)
            .Replace("%SESSION%", CurrentSessionId(), StringComparison.OrdinalIgnoreCase)
            .Replace("%SID%", CurrentUserSid(), StringComparison.OrdinalIgnoreCase);

        expanded = Environment.ExpandEnvironmentVariables(expanded);
        return Path.GetFullPath(expanded);
    }

    private static bool IsNonBoxSection(string name) =>
        NonBoxSections.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    private static string CurrentSessionId()
    {
        try
        {
            return System.Diagnostics.Process.GetCurrentProcess().SessionId.ToString();
        }
        catch
        {
            return "1";
        }
    }

    private static string CurrentUserSid()
    {
        if (!OperatingSystem.IsWindows())
        {
            return string.Empty;
        }

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.User?.Value ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>解析 BoxGrouping=Parent:ChildA,ChildB 与 BoxGrouping=:A,B 两种写法。</summary>
    private static Dictionary<string, List<string>> BuildGroupTree(IniDocument document)
    {
        var tree = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var section in document.FindByPrefix("UserSettings"))
        {
            foreach (var grouping in section.GetValues("BoxGrouping"))
            {
                var colon = grouping.IndexOf(':');
                if (colon < 0)
                {
                    continue;
                }

                var parent = grouping[..colon].Trim();
                var children = grouping[(colon + 1)..]
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(c => c.Length > 0)
                    .ToList();

                if (!tree.TryGetValue(parent, out var existing))
                {
                    existing = new List<string>();
                    tree[parent] = existing;
                }

                foreach (var child in children.Where(c => !existing.Contains(c, StringComparer.OrdinalIgnoreCase)))
                {
                    existing.Add(child);
                }
            }
        }

        return tree;
    }

    private static Dictionary<string, IReadOnlyList<string>> FlattenGroupTree(Dictionary<string, List<string>> tree)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Visit(string parent, List<string> path)
        {
            if (!tree.TryGetValue(parent, out var children) || !visiting.Add(parent))
            {
                return;
            }

            foreach (var child in children)
            {
                var childPath = path.Count == 0 && parent.Length == 0
                    ? new List<string>()
                    : new List<string>(path) { parent };
                result[child] = childPath;
                Visit(child, childPath);
            }

            visiting.Remove(parent);
        }

        Visit(string.Empty, new List<string>());
        return result;
    }

    private static IEnumerable<string> FlattenOrder(Dictionary<string, List<string>> tree, IniDocument document)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<string>();

        void PushRange(IEnumerable<string> items)
        {
            foreach (var item in items.Reverse())
            {
                stack.Push(item);
            }
        }

        if (tree.TryGetValue(string.Empty, out var roots))
        {
            PushRange(roots);
        }

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!seen.Add(current))
            {
                continue;
            }

            yield return current;

            if (tree.TryGetValue(current, out var children))
            {
                PushRange(children);
            }
        }
    }

    private static IEnumerable<SandboxBox> DiscoverUnregisteredBoxes(string template, HashSet<string> known)
    {
        var parent = TryGetTemplateParent(template);
        if (parent is null || !Directory.Exists(parent))
        {
            yield break;
        }

        foreach (var directory in EnumerateDirectoriesSafe(parent))
        {
            var name = Path.GetFileName(directory);
            if (name.Length == 0 || known.Contains(name) || name.StartsWith('.'))
            {
                continue;
            }

            yield return new SandboxBox
            {
                Name = name,
                Enabled = true,
                RootPath = directory,
                FileRootPathTemplate = null,
            };
        }
    }

    private static string? TryGetTemplateParent(string template)
    {
        try
        {
            var probe = template
                .Replace("%SANDBOX%", "probe", StringComparison.OrdinalIgnoreCase)
                .Replace("%USER%", Environment.UserName, StringComparison.OrdinalIgnoreCase)
                .Replace("%SESSION%", "1", StringComparison.OrdinalIgnoreCase)
                .Replace("%SID%", "probe", StringComparison.OrdinalIgnoreCase);
            probe = Environment.ExpandEnvironmentVariables(probe);
            return Path.GetDirectoryName(Path.GetFullPath(probe));
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string> EnumerateDirectoriesSafe(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path).ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
