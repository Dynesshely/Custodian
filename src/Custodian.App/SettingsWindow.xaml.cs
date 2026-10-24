using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Custodian.App.Services;
using Custodian.Interop;

namespace Custodian.App;

/// <summary>
/// 独立设置窗口：左侧 Tabs（外观 / 沙盒），右侧内容区。
/// 所有改动通过 <see cref="SettingsCoordinator"/> 立刻落到运行中的实例，并写入配置
/// （正式实例 ~/.custodian；开发实例写到临时目录，见 <see cref="Custodian.Core.Settings.StoragePaths"/>）。
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsCoordinator _coordinator;
    private bool _loading = true;

    public SettingsWindow(SettingsCoordinator coordinator)
    {
        _coordinator = coordinator;
        InitializeComponent();

        UpdateStorageHint();

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ReloadFromSettings();
        _loading = false;
    }

    // ---------------------------------------------------------------- 初始化

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        TryLoadIcon();

        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (!SafeMode.Has("mica") && hwnd != IntPtr.Zero)
            {
                WindowBackdrop.TryApply(hwnd, BackdropKind.Mica, Themes.ThemeManager.IsDark);
                RootBorder.Background = null;
            }
        }
        catch
        {
            // 没有 Mica 时保留主题底色
        }

        Themes.ThemeManager.Changed += OnThemeChanged;
        Closed += (_, _) => Themes.ThemeManager.Changed -= OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                WindowBackdrop.SetDarkMode(hwnd, Themes.ThemeManager.IsDark);
            }
        }
        catch
        {
            // 忽略：主题已经通过 DynamicResource 生效
        }
    }

    private void TryLoadIcon()
    {
        try
        {
            var resource = Application.GetResourceStream(new Uri("Assets/custodian.ico", UriKind.Relative));
            if (resource is null)
            {
                return;
            }

            using var stream = resource.Stream;
            var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames.OrderByDescending(f => f.PixelWidth).FirstOrDefault();
            if (frame is not null)
            {
                TitleIcon.Source = frame;
                Icon = frame;
            }
        }
        catch
        {
            // 图标缺失不影响设置
        }
    }

    private void UpdateStorageHint()
    {
        StorageHint.Text = _coordinator.IsDevelopment
            ? "开发实例\n读 ~/.custodian\n写 %TEMP%\\custodian-dev"
            : "配置文件\n~/.custodian\\settings.json";
        StorageHint.ToolTip = _coordinator.IsDevelopment
            ? "读取：" + _coordinator.ReadPath + "\n写入：" + _coordinator.WritePath
            : _coordinator.WritePath;
    }

    /// <summary>外部改动（调试通道 / 设置被别处修改）后刷新界面。</summary>
    public void RefreshFromSettings()
    {
        _loading = true;
        ReloadFromSettings();
        _loading = false;
    }

    private void ReloadFromSettings()
    {
        var theme = _coordinator.ThemeMode;
        ThemeSystem.IsChecked = theme == "system";
        ThemeLight.IsChecked = theme == "light";
        ThemeDark.IsChecked = theme == "dark";

        ViewStandard.IsChecked = !_coordinator.IsCompact;
        ViewCompact.IsChecked = _coordinator.IsCompact;

        SandboxRootBox.Text = _coordinator.SandboxRootOverride ?? string.Empty;
        UpdateEffectiveRoot();
    }

    private void UpdateEffectiveRoot()
    {
        var template = string.IsNullOrWhiteSpace(_coordinator.SandboxRootTemplate)
            ? "（尚未扫描）"
            : _coordinator.SandboxRootTemplate;

        EffectiveRootText.Text = template;

        var source = _coordinator.SandboxRootSource switch
        {
            "custom" => "来源：设置里的自定义位置",
            "ini" => "来源：Sandboxie.ini 的 GlobalSettings / FileRootPath",
            _ => "来源：内置默认值",
        };

        var example = template
            .Replace("%SANDBOX%", "<沙盒>", StringComparison.OrdinalIgnoreCase)
            .Replace("%USER%", Environment.UserName, StringComparison.OrdinalIgnoreCase)
            .Replace("%SESSION%", "1", StringComparison.OrdinalIgnoreCase);

        EffectiveRootSourceText.Text = source + "\n展开后形如：" + example;

        SandboxIniPathText.Text = "Sandboxie.ini：" +
            (string.IsNullOrEmpty(_coordinator.SandboxIniPath) ? "（未找到）" : _coordinator.SandboxIniPath);
    }

    /// <summary>切到指定 tab（自测 / 调试通道用）。</summary>
    public void SelectTab(string key)
    {
        var sandbox = string.Equals(key, "sandbox", StringComparison.OrdinalIgnoreCase);
        if (sandbox)
        {
            TabSandbox.IsChecked = true;
        }
        else
        {
            TabAppearance.IsChecked = true;
        }
    }

    // ---------------------------------------------------------------- 交互

    private void OnTabChecked(object sender, RoutedEventArgs e)
    {
        if (AppearancePanel is null || SandboxPanel is null)
        {
            return;
        }

        var sandbox = string.Equals((sender as RadioButton)?.Tag as string, "sandbox", StringComparison.OrdinalIgnoreCase);
        AppearancePanel.Visibility = sandbox ? Visibility.Collapsed : Visibility.Visible;
        SandboxPanel.Visibility = sandbox ? Visibility.Visible : Visibility.Collapsed;

        if (sandbox)
        {
            UpdateEffectiveRoot();
        }
    }

    private void OnThemeChecked(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton { Tag: string mode })
        {
            return;
        }

        _coordinator.SetTheme(mode);
    }

    private void OnViewChecked(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton { Tag: string mode })
        {
            return;
        }

        _coordinator.SetCompact(mode == "compact");
    }

    private async void OnBrowseSandboxRoot(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择 Sandboxie 沙盒存储位置",
            Multiselect = false,
        };

        var current = _coordinator.SandboxRootOverride;
        if (!string.IsNullOrWhiteSpace(current) && System.IO.Directory.Exists(current))
        {
            dialog.InitialDirectory = current;
        }

        if (dialog.ShowDialog(this) == true)
        {
            SandboxRootBox.Text = dialog.FolderName;
            await ApplySandboxRootAsync();
        }
    }

    private async void OnApplySandboxRoot(object sender, RoutedEventArgs e) => await ApplySandboxRootAsync();

    private async void OnResetSandboxRoot(object sender, RoutedEventArgs e)
    {
        SandboxRootBox.Text = string.Empty;
        await ApplySandboxRootAsync();
    }

    private void OnSandboxRootKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            _ = ApplySandboxRootAsync();
        }
    }

    private async System.Threading.Tasks.Task ApplySandboxRootAsync()
    {
        ApplySandboxRootButton.IsEnabled = false;
        try
        {
            await _coordinator.SetSandboxRootAsync(SandboxRootBox.Text);
            SandboxRootBox.Text = _coordinator.SandboxRootOverride ?? string.Empty;
            UpdateEffectiveRoot();
            SandboxRootHint.Text = "已保存并重新扫描沙盒。";
        }
        catch (Exception ex)
        {
            SandboxRootHint.Text = "应用失败：" + ex.Message;
        }
        finally
        {
            ApplySandboxRootButton.IsEnabled = true;
        }
    }

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch
        {
            // 拖动失败（快速双击等）忽略
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }
}
