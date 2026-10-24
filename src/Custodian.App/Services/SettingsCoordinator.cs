using Custodian.App.ViewModels;
using Custodian.Core.Settings;

namespace Custodian.App.Services;

/// <summary>
/// 设置窗口与运行中应用之间的中介：把「外观 / 卡片视图 / 沙盒存储位置」的改动立刻落到正在跑的实例上，
/// 并持久化到 <see cref="StoragePaths.WriteSettingsPath"/>（开发实例写临时目录）。
/// </summary>
public sealed class SettingsCoordinator
{
    private readonly MainViewModel _vm;
    private readonly Func<Task> _reload;

    public SettingsCoordinator(MainViewModel viewModel, Func<Task> reload)
    {
        _vm = viewModel;
        _reload = reload;
    }

    private CustodianSettings Settings => _vm.Settings;

    public string ThemeMode => CustodianSettings.NormalizeTheme(Settings.Theme);

    public bool IsCompact => _vm.IsCompactView;

    public string WritePath => StoragePaths.WriteSettingsPath;

    public string ReadPath => StoragePaths.ReadSettingsPath;

    public bool IsDevelopment => StoragePaths.IsDevelopment;

    /// <summary>设置里自定义的根目录（null/空 = 跟随 Sandboxie.ini）。</summary>
    public string? SandboxRootOverride => Settings.SandboxRootPath;

    /// <summary>最近一次扫描实际使用的 FileRootPath 模板。</summary>
    public string SandboxRootTemplate => _vm.SandboxRootTemplate;

    /// <summary>根目录来源：custom / ini / default。</summary>
    public string SandboxRootSource => _vm.SandboxRootSource;

    public string SandboxIniPath => _vm.SandboxIniPath;

    /// <summary>改主题：立刻生效并保存。</summary>
    public void SetTheme(string mode)
    {
        var normalized = CustodianSettings.NormalizeTheme(mode);
        Settings.Theme = normalized;
        Settings.Save();
        Themes.ThemeManager.ApplyPreference(normalized);
    }

    /// <summary>改卡片视图（standard / compact）。</summary>
    public void SetCompact(bool compact)
    {
        if (_vm.SetView(compact))
        {
            return;
        }
    }

    /// <summary>改沙盒存储位置：保存后重新扫描沙盒。</summary>
    public async Task SetSandboxRootAsync(string? path)
    {
        var normalized = string.IsNullOrWhiteSpace(path) ? null : path.Trim().TrimEnd('\\');
        if (string.Equals(Settings.SandboxRootPath, normalized, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Settings.SandboxRootPath = normalized;
        Settings.Save();
        await _reload();
    }
}
