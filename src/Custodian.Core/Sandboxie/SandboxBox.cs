namespace Custodian.Core.Sandboxie;

/// <summary>Sandboxie.ini 里的一个沙盒（box）。</summary>
public sealed class SandboxBox
{
    public required string Name { get; init; }

    public bool Enabled { get; init; } = true;

    /// <summary>展开变量后的沙盒根目录（可能不存在）。</summary>
    public required string RootPath { get; init; }

    /// <summary>原始 FileRootPath 模板（若该 box 自己覆盖了全局值）。</summary>
    public string? FileRootPathTemplate { get; init; }

    /// <summary>该 box 在分组树中的层级路径，例如 ["Mainland"] 或空。</summary>
    public IReadOnlyList<string> GroupPath { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Templates { get; init; } = Array.Empty<string>();

    public bool RootExists => Directory.Exists(RootPath);

    public string DisplayGroup => GroupPath.Count == 0 ? string.Empty : string.Join(" / ", GroupPath);
}
