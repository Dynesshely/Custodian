using System.Diagnostics;
using Custodian.Core.Discovery;

namespace Custodian.Core.Launching;

/// <summary>通过 Sandboxie 的 Start.exe 在指定沙盒里启动目标程序。</summary>
public static class AppLauncher
{
    private static readonly Lazy<string?> StartExePath = new(ResolveStartExe, isThreadSafe: true);

    public static string? StartExe => StartExePath.Value;

    public static bool TryLaunch(DiscoveredApp app, out string? error)
    {
        var startExe = StartExe;
        if (startExe is null)
        {
            error = "未找到 Sandboxie 的 Start.exe";
            return false;
        }

        try
        {
            var arguments = BuildArguments(app);
            var info = new ProcessStartInfo(startExe)
            {
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Directory.Exists(app.WorkingDirectory) ? app.WorkingDirectory : string.Empty,
            };

            using var process = Process.Start(info);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static string BuildArguments(DiscoveredApp app)
    {
        var builder = new System.Text.StringBuilder();
        builder.Append("/box:").Append(app.SandboxName).Append(" /silent ");
        builder.Append('"').Append(app.TargetPath).Append('"');

        if (!string.IsNullOrWhiteSpace(app.Arguments))
        {
            builder.Append(' ').Append(app.Arguments);
        }

        return builder.ToString();
    }

    private static string? ResolveStartExe()
    {
        var candidates = new List<string>();

        foreach (var programFiles in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                 })
        {
            if (!string.IsNullOrEmpty(programFiles))
            {
                candidates.Add(Path.Combine(programFiles, "Sandboxie-Plus", "Start.exe"));
                candidates.Add(Path.Combine(programFiles, "Sandboxie", "Start.exe"));
            }
        }

        candidates.Add(Path.Combine(AppContext.BaseDirectory, "Start.exe"));

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
