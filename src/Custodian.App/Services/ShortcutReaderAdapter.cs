using Custodian.Core.Discovery;
using Custodian.Interop;

namespace Custodian.App.Services;

/// <summary>把 Interop 的 IShellLink 解析包装成 Core 需要的接口。</summary>
public sealed class ShortcutReaderAdapter : IShortcutReader
{
    public ShortcutTarget? Read(string shortcutPath)
    {
        var info = ShortcutResolver.Resolve(shortcutPath);
        if (info is null || string.IsNullOrWhiteSpace(info.TargetPath))
        {
            return null;
        }

        return new ShortcutTarget(
            info.TargetPath,
            info.Arguments ?? string.Empty,
            info.WorkingDirectory ?? string.Empty);
    }
}
