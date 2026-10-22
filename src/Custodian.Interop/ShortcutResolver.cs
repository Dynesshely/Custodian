using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Custodian.Interop.Native;

namespace Custodian.Interop;

public sealed record ShellLinkInfo(string TargetPath, string Arguments, string WorkingDirectory);

/// <summary>
/// Resolves .lnk files through IShellLinkW / IPersistFile.
/// The link is only read, never resolved against the target, so links whose target is missing
/// (typical inside a sandbox) still parse successfully.
/// </summary>
public static class ShortcutResolver
{
    private const uint SLGP_RAWPATH = 0x00000004;
    private const uint STGM_READ = 0x00000000;
    private static readonly Guid ShellLinkClassId = new("00021401-0000-0000-C000-000000000046");

    public static ShellLinkInfo? Resolve(string lnkPath)
    {
        try
        {
            var fullPath = Normalize(lnkPath);
            if (fullPath is null || !File.Exists(fullPath))
            {
                return null;
            }

            var link = CreateShellLink();
            if (link is null)
            {
                return null;
            }

            ((IPersistFile)link).Load(fullPath, STGM_READ);

            var target = ReadString((buffer, capacity) => link.GetPath(buffer, capacity, IntPtr.Zero, SLGP_RAWPATH));
            if (string.IsNullOrWhiteSpace(target))
            {
                target = ReadString((buffer, capacity) => link.GetPath(buffer, capacity, IntPtr.Zero, 0));
            }

            if (string.IsNullOrWhiteSpace(target))
            {
                // Shell links may target a shell namespace item instead of a file (e.g. "This PC").
                // Try to project that item list onto the file system; purely virtual items stay unresolved.
                target = TryResolveIdList(link);
            }

            if (string.IsNullOrWhiteSpace(target))
            {
                return null;
            }

            var arguments = ReadString((buffer, capacity) => link.GetArguments(buffer, capacity));
            var workingDirectory = ReadString((buffer, capacity) => link.GetWorkingDirectory(buffer, capacity));

            return new ShellLinkInfo(
                ExpandEnvironment(target),
                arguments,
                ExpandEnvironment(workingDirectory));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Reads the icon location stored in the link (used by the icon loader, not part of the public contract).</summary>
    internal static bool TryGetIconLocation(string lnkPath, out string? iconPath, out int iconIndex)
    {
        iconPath = null;
        iconIndex = 0;

        try
        {
            var fullPath = Normalize(lnkPath);
            if (fullPath is null || !File.Exists(fullPath))
            {
                return false;
            }

            var link = CreateShellLink();
            if (link is null)
            {
                return false;
            }

            ((IPersistFile)link).Load(fullPath, STGM_READ);
            var index = 0;
            var location = ReadString((buffer, capacity) => link.GetIconLocation(buffer, capacity, out index));
            iconIndex = index;
            if (string.IsNullOrWhiteSpace(location))
            {
                return false;
            }

            iconPath = ExpandEnvironment(location);
            return iconPath.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static string TryResolveIdList(IShellLinkW link)
    {
        var pidl = IntPtr.Zero;
        try
        {
            link.GetIDList(out pidl);
            if (pidl == IntPtr.Zero)
            {
                return string.Empty;
            }

            var buffer = new StringBuilder(260);
            return NativeMethods.SHGetPathFromIDList(pidl, buffer) != 0 ? buffer.ToString() : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
        finally
        {
            if (pidl != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(pidl);
            }
        }
    }

    private static IShellLinkW? CreateShellLink()
    {
        try
        {
            var type = Type.GetTypeFromCLSID(ShellLinkClassId, throwOnError: false);
            if (type is null)
            {
                return null;
            }

            return Activator.CreateInstance(type) as IShellLinkW;
        }
        catch
        {
            return null;
        }
    }

    private static string? Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var trimmed = path.Trim().Trim('"');
        if (trimmed.Length == 0)
        {
            return null;
        }

        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(trimmed));
    }

    private static string ExpandEnvironment(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var expanded = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
        return expanded.Trim();
    }

    private delegate void StringReader(StringBuilder buffer, int capacity);

    private static string ReadString(StringReader read)
    {
        for (var capacity = 512; capacity <= 32768; capacity *= 2)
        {
            var buffer = new StringBuilder(capacity);
            read(buffer, capacity);
            var value = buffer.ToString();
            if (value.Length < capacity - 1)
            {
                return value;
            }
        }

        return string.Empty;
    }
}
