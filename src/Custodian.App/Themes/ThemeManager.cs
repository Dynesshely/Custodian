using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Custodian.App.Themes;

/// <summary>
/// 浅色 / 深色主题管理。
///
/// 实现方式（对应 docs/CONTRACTS.md 3.5）：
///   * <c>Themes/Light.xaml</c> / <c>Themes/Dark.xaml</c> 是两份**纯数据**画刷表，键完全一致；
///     <c>Fluent.xaml</c> 把两者都合并进来，所以说样式（Style / ControlTemplate）只写一份。
///   * <see cref="Initialize"/> 在 Application.Resources 末尾再挂一层「替换字典」(<c>_live</c>)，
///     里面是从当前主题数据表克隆出来的画刷/颜色；界面所有 DynamicResource 最终都解析到这一层。
///   * 切换主题 = 重建这层替换字典并换上（同键不同值），不重建控件、不改样式。
///     WPF 会把资源字典的变更通知给已连上 PresentationSource 的元素，DynamicResource 自动重新取值
///     （注意：离屏裸视觉树不参与这套通知，用 RenderTargetBitmap 凭空渲染时切主题不会刷新 ——
///      所以 tools/ThemeLab 会先把样例页放进一个透明离屏窗口里再渲染）。
///   * 启动时读注册表 AppsUseLightTheme；运行中监听 SystemEvents.UserPreferenceChanged 跟随系统切换。
///
/// 使用顺序：Application.Resources 合并 Fallback.xaml → Fluent.xaml → 再调用 <see cref="Initialize"/>。
///
/// 契约 API：<c>IsDark</c> / <c>Initialize()</c> / <c>Changed</c>
/// （另外提供 <c>IsInitialized</c> / <c>SetDark</c> / <c>RefreshFromSystem</c> / <c>LastError</c> 便于调试与自测）。
/// </summary>
public static class ThemeManager
{
    private const string PersonalizeKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightThemeValue = "AppsUseLightTheme";

    /// <summary>Light.xaml / Dark.xaml 里的主题标识键，用于在字典树里认出深浅色数据表。</summary>
    private const string ThemeVariantKey = "ThemeVariant";
    private const string LightVariant = "Light";
    private const string DarkVariant = "Dark";

    private static readonly object SyncRoot = new();

    private static bool _initialized;
    private static bool _isDark;

    /// <summary>浅色 / 深色数据表。</summary>
    private static ResourceDictionary? _lightDictionary;
    private static ResourceDictionary? _darkDictionary;

    /// <summary>运行时画刷层：应用在 Application.Resources 最上面，切换主题时原地改色。</summary>
    private static ResourceDictionary? _liveDictionary;

    /// <summary>当前是否为深色主题。</summary>
    public static bool IsDark
    {
        get { lock (SyncRoot) { return _isDark; } }
    }

    /// <summary>是否已经初始化过（重复调用 <see cref="Initialize"/> 无副作用）。</summary>
    public static bool IsInitialized
    {
        get { lock (SyncRoot) { return _initialized; } }
    }

    /// <summary>主题真正发生变化时触发（系统主题变化或调用 <see cref="SetDark"/>）。</summary>
    public static event EventHandler? Changed;

    /// <summary>最近一次内部错误（诊断用；正常为 null）。</summary>
    public static string? LastError { get; private set; }

    /// <summary>应用替换字典的次数（诊断/自测用）。</summary>
    public static int ApplyCount { get; private set; }

    /// <summary>
    /// 是否跟随系统主题变化。默认 true；调试、截图、或将来做「强制深/浅色」设置时可以关掉，
    /// 关掉后系统偏好变化不会再自动切换（手动 <see cref="SetDark"/> 仍然有效）。
    /// </summary>
    public static bool FollowSystem { get; set; } = true;

    /// <summary>
    /// 在 Application.Resources 已经合并 Fluent.xaml 之后调用一次：
    /// 叠上运行时画刷层、按系统设置选中深浅色，并开始监听系统主题变化。应用退出时自动注销监听。
    /// </summary>
    public static void Initialize()
    {
        lock (SyncRoot)
        {
            if (_initialized) return;
            _initialized = true;
        }

        _isDark = ReadSystemIsDark();

        var app = Application.Current;
        if (app is null)
        {
            Debug.WriteLine("[ThemeManager] Application.Current 为空，跳过初始化。");
            return;
        }

        DiscoverSourceDictionaries(app.Resources);
        ApplyVariant(_isDark, raiseChanged: false);

        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        app.Exit += OnApplicationExit;
    }

    /// <summary>手动设置主题（契约外的小扩展：调试预览、跟随应用内设置等）。只换画刷值，不重建控件。</summary>
    public static void SetDark(bool isDark)
    {
        bool changed;
        lock (SyncRoot)
        {
            changed = _isDark != isDark;
            _isDark = isDark;
        }

        if (changed)
        {
            ApplyVariant(isDark, raiseChanged: true);
        }
        else if (_liveDictionary is null)
        {
            // 主题没变但替换字典还没建立（例如 Initialize 时还没有 Application）：补一次。
            ApplyVariant(isDark, raiseChanged: false);
        }
    }

    /// <summary>
    /// 按应用内设置应用主题：<c>system</c> 跟随系统，<c>light</c> / <c>dark</c> 强制。
    /// 设置窗口与启动流程共用这一个入口。
    /// </summary>
    public static void ApplyPreference(string? mode)
    {
        switch ((mode ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "light":
                FollowSystem = false;
                SetDark(false);
                break;

            case "dark":
                FollowSystem = false;
                SetDark(true);
                break;

            default:
                FollowSystem = true;
                RefreshFromSystem();
                break;
        }
    }

    /// <summary>重新读取系统主题；返回是否发生了变化。</summary>
    public static bool RefreshFromSystem()
    {
        var dark = ReadSystemIsDark();
        bool changed;
        lock (SyncRoot)
        {
            changed = _isDark != dark;
            _isDark = dark;
        }

        if (changed)
        {
            ApplyVariant(dark, raiseChanged: true);
        }

        return changed;
    }

    // ------------------------------------------------------------------ 系统主题

    /// <summary>读注册表：AppsUseLightTheme == 0 表示深色。读不到按浅色处理。</summary>
    private static bool ReadSystemIsDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath, writable: false);
            var value = key?.GetValue(AppsUseLightThemeValue);
            if (value is int i) return i == 0;
            if (value is long l) return l == 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ThemeManager] 读取系统主题失败：{ex.Message}");
        }

        return false;
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (!FollowSystem) return;

        var app = Application.Current;
        if (app is null)
        {
            RefreshFromSystem();
            return;
        }

        var dispatcher = app.Dispatcher;
        if (dispatcher.CheckAccess())
        {
            RefreshFromSystem();
        }
        else
        {
            dispatcher.BeginInvoke(new Action(() => RefreshFromSystem()));
        }
    }

    private static void OnApplicationExit(object? sender, ExitEventArgs e)
    {
        try
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ThemeManager] 注销系统事件失败：{ex.Message}");
        }
    }

    // ------------------------------------------------------------------ 字典发现

    /// <summary>
    /// 在资源字典树里找 Light/Dark 数据表。字典的 <c>Contains</c> 会连合并字典一起查，
    /// 所以判断变体必须只看「自己的键」；顺带避免把上层字典误认成变体表。
    /// </summary>
    private static void DiscoverSourceDictionaries(ResourceDictionary root)
    {
        _lightDictionary = FindVariant(root, LightVariant);
        _darkDictionary = FindVariant(root, DarkVariant);

        if (_lightDictionary is not null && _darkDictionary is not null) return;

        // 兜底：Application.Resources 里没合并 Fluent 时，按约定路径自己加载。
        try
        {
            _lightDictionary ??= new ResourceDictionary { Source = new Uri("Themes/Light.xaml", UriKind.Relative) };
            _darkDictionary ??= new ResourceDictionary { Source = new Uri("Themes/Dark.xaml", UriKind.Relative) };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ThemeManager] 加载主题数据表失败：{ex.Message}");
        }
    }

    private static ResourceDictionary? FindVariant(ResourceDictionary root, string variant)
    {
        if (MatchesVariant(root, variant)) return root;

        foreach (var child in root.MergedDictionaries)
        {
            if (MatchesVariant(child, variant)) return child;

            var nested = FindVariant(child, variant);
            if (nested is not null) return nested;
        }

        return null;
    }

    /// <summary>只检查字典自己的键（不能用 Contains：它会连合并字典一起查）。</summary>
    private static bool MatchesVariant(ResourceDictionary dictionary, string variant)
    {
        try
        {
            foreach (var key in dictionary.Keys)
            {
                if (key is not string name || !string.Equals(name, ThemeVariantKey, StringComparison.Ordinal))
                {
                    continue;
                }

                return dictionary[name] is string value &&
                       string.Equals(value, variant, StringComparison.OrdinalIgnoreCase);
            }
        }
        catch
        {
            // 忽略个别字典的探测异常
        }

        return false;
    }

    // ------------------------------------------------------------------ 运行时画刷层

    /// <summary>
    /// 按当前主题重建一层「替换字典」并挂到 Application.Resources 末尾（后合并者优先）。
    /// 只做资源替换，不碰任何控件；WPF 会在字典层变化时让 DynamicResource 重新取值。
    /// </summary>
    private static void ApplyVariant(bool dark, bool raiseChanged)
    {
        var app = Application.Current;
        var source = dark ? _darkDictionary : _lightDictionary;

        if (app is null || source is null)
        {
            LastError = "数据表或 Application 不可用";
            if (raiseChanged) RaiseChanged();
            return;
        }

        try
        {
            var live = new ResourceDictionary();
            foreach (var key in source.Keys)
            {
                if (key is not string name || string.Equals(name, ThemeVariantKey, StringComparison.Ordinal))
                {
                    continue;
                }

                var value = source[name];
                live[name] = value is Freezable freezable ? freezable.CloneCurrentValue() : value;
            }

            var merged = app.Resources.MergedDictionaries;
            if (_liveDictionary is not null)
            {
                merged.Remove(_liveDictionary);
            }

            merged.Add(live);
            _liveDictionary = live;
            LastError = null;
            ApplyCount++;
        }
        catch (Exception ex)
        {
            LastError = ex.GetType().Name + ": " + ex.Message;
            Debug.WriteLine($"[ThemeManager] 应用主题失败：{ex}");
        }

        if (raiseChanged) RaiseChanged();
    }

    private static void RaiseChanged()
    {
        var handler = Changed;
        if (handler is null) return;

        var app = Application.Current;
        if (app is null || app.Dispatcher.CheckAccess())
        {
            handler(null, EventArgs.Empty);
            return;
        }

        app.Dispatcher.BeginInvoke(new Action(() => handler(null, EventArgs.Empty)));
    }
}