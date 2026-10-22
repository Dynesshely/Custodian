using System.IO;

namespace Custodian.App.Services;

/// <summary>轻量诊断日志：%LOCALAPPDATA%\Custodian\startup.log（追加）。</summary>
public static class Diagnostics
{
    private static readonly object Gate = new();

    public static string LogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Custodian",
        "startup.log");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // 诊断失败不影响主流程
        }
    }
}
