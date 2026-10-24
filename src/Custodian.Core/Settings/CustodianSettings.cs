using System.Text.Json;
using System.Text.Json.Serialization;

namespace Custodian.Core.Settings;

/// <summary>用户设置，落在 <c>~/.custodian/settings.json</c>（开发实例写到临时目录，见 <see cref="StoragePaths"/>）。</summary>
public sealed class CustodianSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>全局热键文本，例如 Ctrl+Alt+Space。</summary>
    public string Hotkey { get; set; } = "Ctrl+Alt+Space";

    /// <summary>启动应用后是否把面板收起到托盘。</summary>
    public bool HideOnLaunch { get; set; } = true;

    /// <summary>上次选中的沙盒名。</summary>
    public string? SelectedSandbox { get; set; }

    /// <summary>是否展示没有任何应用的沙盒。</summary>
    public bool ShowEmptySandboxes { get; set; }

    /// <summary>卡片视图："standard"（默认，图标 + 名称 + 路径）或 "compact"（图标 + 标题网格）。</summary>
    public string CardView { get; set; } = "standard";

    /// <summary>主题："system"（默认）/"light"/"dark"。</summary>
    public string Theme { get; set; } = "system";

    /// <summary>
    /// 自定义 Sandboxie 沙盒存储位置（FileRootPath 模板）。
    /// 为空表示跟随 Sandboxie.ini 的 GlobalSettings/FileRootPath（再不行就用默认值）。
    /// </summary>
    public string? SandboxRootPath { get; set; }

    /// <summary>旧版配置位置（%LOCALAPPDATA%\Custodian\settings.json），仅用于一次性迁移。</summary>
    [JsonIgnore]
    public static string LegacyFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Custodian",
        "settings.json");

    /// <summary>当前进程实际写入的配置文件。</summary>
    [JsonIgnore]
    public static string FilePath => StoragePaths.WriteSettingsPath;

    public static CustodianSettings Load()
    {
        var loaded = TryLoad(StoragePaths.ReadSettingsPath);

        // 正式实例从旧的 %LOCALAPPDATA% 位置迁移一次
        if (loaded is null && !StoragePaths.IsDevelopment)
        {
            loaded = TryLoad(LegacyFilePath);
            if (loaded is not null)
            {
                loaded.Save();
            }
        }

        return loaded ?? new CustodianSettings();
    }

    private static CustodianSettings? TryLoad(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<CustodianSettings>(json, JsonOptions);
        }
        catch
        {
            // 配置损坏时回退默认值
            return null;
        }
    }

    public void Save()
    {
        try
        {
            var path = StoragePaths.WriteSettingsPath;
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch
        {
            // 保存失败不阻塞 UI
        }
    }

    /// <summary>把主题设置归一化成 system / light / dark。</summary>
    public static string NormalizeTheme(string? theme) => (theme ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "light" => "light",
        "dark" => "dark",
        _ => "system",
    };
}
