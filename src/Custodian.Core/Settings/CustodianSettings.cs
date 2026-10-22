using System.Text.Json;
using System.Text.Json.Serialization;

namespace Custodian.Core.Settings;

/// <summary>用户设置，落在 %LOCALAPPDATA%\Custodian\settings.json。</summary>
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

    [JsonIgnore]
    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Custodian",
        "settings.json");

    public static CustodianSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var settings = JsonSerializer.Deserialize<CustodianSettings>(json, JsonOptions);
                if (settings is not null)
                {
                    return settings;
                }
            }
        }
        catch
        {
            // 配置损坏时回退默认值
        }

        return new CustodianSettings();
    }

    public void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch
        {
            // 保存失败不阻塞 UI
        }
    }
}
