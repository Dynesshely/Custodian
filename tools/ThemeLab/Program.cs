using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Custodian.App.Themes;

namespace ThemeLab;

internal static class Program
{
    private const int LogicalWidth = 1200;
    private const int LogicalHeight = 800;

    /// <summary>docs/CONTRACTS.md 第 3 节的键清单（改契约时同步改这里）。</summary>
    private static readonly string[] BrushKeys =
    {
        "AccentBrush", "AccentColor",
        "LayerFillBrush", "LayerFillHoverBrush", "LayerFillPressedBrush", "LayerFillSelectedBrush",
        "LayerBorderBrush", "LayerBorderHoverBrush",
        "TextPrimaryBrush", "TextSecondaryBrush", "TextTertiaryBrush", "TextOnAccentBrush",
        "ControlFillBrush", "ControlFillHoverBrush", "ControlBorderBrush", "ControlBorderFocusBrush",
        "SubtleHoverBrush", "SubtlePressedBrush",
        "RailSelectedIndicatorBrush", "RailSelectedBrush",
        "ScrollThumbBrush", "ScrollThumbHoverBrush",
        "WindowOutlineBrush", "SeparatorBrush",
        "ToolTipBackgroundBrush", "ToolTipBorderBrush", "ToolTipTextBrush",
        "IconMonoBrush",
    };

    private static readonly (string Key, string TargetType)[] StyleKeys =
    {
        ("LauncherCardStyle", "Button"),
        ("RailItemStyle", "RadioButton"),
        ("IconButtonStyle", "Button"),
        ("FilterTextBoxStyle", "TextBox"),
        ("ThinScrollBarStyle", "ScrollBar"),
        ("ScrollViewerStyle", "ScrollViewer"),
        ("FluentToolTipStyle", "ToolTip"),
        ("WindowDragThumbStyle", "Thumb"),
        ("AppFontFamily", "FontFamily"),
        ("TextStyleTitle", "TextBlock"),
        ("TextStyleBody", "TextBlock"),
        ("TextStyleBodyStrong", "TextBlock"),
        ("TextStyleCaption", "TextBlock"),
        ("TextStyleSection", "TextBlock"),
        ("TextStyleHint", "TextBlock"),
    };

    private static readonly StringBuilder Report = new();

    [STAThread]
    private static int Main(string[] args)
    {
        var mode = (args.Length > 0 ? args[0] : "render").ToLowerInvariant();

        try
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            LoadThemeDictionaries(app);

            // 主题选择之前先抓住两套字典（ThemeManager 之后会从合并树里摘掉没用的那套）。
            var (lightDictionary, darkDictionary) = FindVariantDictionaries(app.Resources);

            if (mode == "raw")
            {
                Console.WriteLine(Dump(app.Resources, 0, new HashSet<ResourceDictionary>()));
                Console.WriteLine("AccentBrush = " + app.TryFindResource("AccentBrush"));
                Console.WriteLine("LauncherCardStyle = " + app.TryFindResource("LauncherCardStyle"));
                return 0;
            }

            ThemeManager.Initialize();

            if (mode is not "show" and not "tooltip")
            {
                // 截图/自测要可重放：不跟随系统主题（系统会异步广播 WM_SETTINGCHANGE，可能中途翻转渲染结果）。
                ThemeManager.FollowSystem = false;
            }

            if (mode == "probe")
            {
                RunProbe();
                return 0;
            }

            if (mode == "show")
            {
                RunLive(app);
                return 0;
            }

            if (mode == "tooltip")
            {
                RunTooltipProbe(app);
                return 0;
            }

            return RenderSnapshots(lightDictionary, darkDictionary);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[FATAL] " + ex);
            return 1;
        }
    }

    private static void LoadThemeDictionaries(Application app)
    {
        try
        {
            app.Resources.MergedDictionaries.Add(
                new ResourceDictionary { Source = new Uri("Themes/Fluent.xaml", UriKind.Relative) });
        }
        catch (Exception ex)
        {
            Line("[WARN] 相对路径加载 Fluent.xaml 失败，改用 pack URI：" + ex.Message);
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/ThemeLab;component/Themes/Fluent.xaml", UriKind.Absolute),
            });
        }
    }

    private static void Line(string text)
    {
        Report.AppendLine(text);
        Console.WriteLine(text);
    }

    /// <summary>诊断：验证动态资源在「同一棵可视树」上随主题切换更新。</summary>
    private static void RunProbe()
    {
        var app = Application.Current;
        var probeBrush = (SolidColorBrush?)app.TryFindResource("TextPrimaryBrush");
        Line("merged 层数=" + app.Resources.MergedDictionaries.Count
             + " brushFrozen=" + (probeBrush?.IsFrozen.ToString() ?? "null")
             + " LastError=" + (ThemeManager.LastError ?? "(none)"));

        var page = new LabPage();
        var host = CreateHost(page);
        Line("窗口内初始(深色): " + page.ProbeColors());

        ThemeManager.SetDark(false);
        Settle(page);
        Line("窗口内切浅色: " + page.ProbeColors());

        ThemeManager.SetDark(true);
        Settle(page);
        Line("窗口内切回深色: " + page.ProbeColors());

        ThemeManager.SetDark(false);
        var fresh = new LabPage();
        var freshHost = CreateHost(fresh);
        Line("浅色下新建页面: " + fresh.ProbeColors());
        Line("滚动接线（未滚动）: " + fresh.ProbeScroll(0));
        Line("滚动接线（滚到 220）: " + fresh.ProbeScroll(220));
        freshHost.Close();

        host.Close();
    }

    private static int RenderSnapshots(ResourceDictionary? lightDictionary, ResourceDictionary? darkDictionary)
    {
        var problems = 0;

        Line("=== Custodian ThemeLab ===");
        Line("ThemeManager.IsDark（注册表 AppsUseLightTheme=0 -> 深色）: " + ThemeManager.IsDark);
        Line("SystemEvents 监听: " + (ThemeManager.IsInitialized ? "已初始化" : "未初始化"));

        // ---------- 1. 契约键完整性 ----------
        Line(string.Empty);
        Line("--- 契约键校验 ---");
        foreach (var key in BrushKeys)
        {
            var value = Application.Current.TryFindResource(key);
            if (value is null)
            {
                Line("[缺失] " + key);
                problems++;
            }
            else if (key == "AccentColor" && value is not Color)
            {
                Line("[类型错] AccentColor 应为 Color，实际 " + value.GetType().Name);
                problems++;
            }
            else if (key != "AccentColor" && value is not SolidColorBrush)
            {
                Line("[类型错] " + key + " 应为 SolidColorBrush，实际 " + value.GetType().Name);
                problems++;
            }
        }

        foreach (var (key, targetType) in StyleKeys)
        {
            var value = Application.Current.TryFindResource(key);
            if (value is null)
            {
                Line("[缺失] " + key);
                problems++;
                continue;
            }

            if (key == "AppFontFamily")
            {
                if (value is not FontFamily) { Line("[类型错] AppFontFamily"); problems++; }
                continue;
            }

            if (value is not Style style)
            {
                Line("[类型错] " + key + " 应为 Style，实际 " + value.GetType().Name);
                problems++;
            }
            else if (style.TargetType?.Name != targetType)
            {
                Line("[TargetType 错] " + key + " 期望 " + targetType + "，实际 " + style.TargetType?.Name);
                problems++;
            }
        }

        Line(problems == 0 ? "全部契约键存在且类型正确。" : "发现 " + problems + " 个问题。");

        // ---------- 2. 深浅色键一致性 ----------
        Line(string.Empty);
        Line("--- Light / Dark 键一致性 ---");
        if (lightDictionary is null || darkDictionary is null)
        {
            Line("[缺失] Fluent.xaml 没有同时合并 Light.xaml / Dark.xaml");
            problems++;
        }
        else
        {
            var lightKeys = lightDictionary.Keys.Cast<object>().Select(k => k.ToString()!).OrderBy(k => k).ToList();
            var darkKeys = darkDictionary.Keys.Cast<object>().Select(k => k.ToString()!).OrderBy(k => k).ToList();
            var onlyLight = lightKeys.Except(darkKeys).ToList();
            var onlyDark = darkKeys.Except(lightKeys).ToList();
            Line("Light 键数 = " + lightKeys.Count + "，Dark 键数 = " + darkKeys.Count);
            if (onlyLight.Count > 0) { Line("仅 Light 有: " + string.Join(", ", onlyLight)); problems++; }
            if (onlyDark.Count > 0) { Line("仅 Dark 有: " + string.Join(", ", onlyDark)); problems++; }
            if (onlyLight.Count == 0 && onlyDark.Count == 0) Line("两套键完全一致。");
        }

        // ---------- 3. Fluent 不依赖 Fallback ----------
        Line(string.Empty);
        Line("--- Fluent 自包含校验（不引用 Fallback 独有键）---");
        problems += CheckSelfContained();

        // ---------- 4. 离屏渲染 ----------
        Line(string.Empty);
        Line("--- 离屏渲染 ---");
        var temp = Path.GetTempPath();
        var lightPng = Path.Combine(temp, "custodian-theme-light.png");
        var darkPng = Path.Combine(temp, "custodian-theme-dark.png");
        var lightDetail = Path.Combine(temp, "custodian-theme-light-detail.png");
        var darkDetail = Path.Combine(temp, "custodian-theme-dark-detail.png");
        var tooltipLight = Path.Combine(temp, "custodian-theme-light-tooltip.png");
        var tooltipDark = Path.Combine(temp, "custodian-theme-dark-tooltip.png");
        var detailRegion = new Rect(16, 12, 770, 468);
        var toolbarRegion = new Rect(880, 20, 304, 120);
        var lightToolbar = Path.Combine(temp, "custodian-theme-light-toolbar.png");
        var darkToolbar = Path.Combine(temp, "custodian-theme-dark-toolbar.png");

        var app = Application.Current;

        var page = new LabPage();
        var host = CreateHost(page);

        // 浅色：同一个可视树先跑浅色，再切深色 —— 顺便验证「只换画刷值、不重建控件」。
        ThemeManager.SetDark(false);
        page.ApplyChrome(false);
        Settle(page);
        page.MarkDemoStates();
        Settle(page);

        var lightTip = CreateToolTipVisual();
        var anchor = page.GetHelpButtonAnchor();

        // 输入框有文字时的样子（检查字体基线 / 光标位置，顺手验证 FilterTextBoxStyle 的 PART_ContentHost）
        page.SetFilterText("code.exe");
        Settle(page);
        var typedPng = Path.Combine(temp, "custodian-theme-filter-typed.png");
        SaveComposite(page, lightTip, anchor, 3.0, typedPng, new Rect(392, 28, 420, 64));
        page.SetFilterText(string.Empty);
        Settle(page);

        Line("浅色渲染前: IsDark=" + ThemeManager.IsDark + " ApplyCount=" + ThemeManager.ApplyCount);
        SaveComposite(page, lightTip, anchor, 1.0, lightPng);
        SaveComposite(page, lightTip, anchor, 2.0, lightDetail, detailRegion);
        SaveComposite(page, lightTip, anchor, 3.0, lightToolbar, toolbarRegion);
        SaveVisual(lightTip, tooltipLight);
        Line("浅色渲染后: IsDark=" + ThemeManager.IsDark + " ApplyCount=" + ThemeManager.ApplyCount);

        ThemeManager.SetDark(true);
        page.ApplyChrome(true);
        Settle(page);

        Line("深色渲染前: IsDark=" + ThemeManager.IsDark + " ApplyCount=" + ThemeManager.ApplyCount);
        var darkTip = CreateToolTipVisual();
        var darkAnchor = page.GetHelpButtonAnchor();
        SaveComposite(page, darkTip, darkAnchor, 1.0, darkPng);
        SaveComposite(page, darkTip, darkAnchor, 2.0, darkDetail, detailRegion);
        SaveComposite(page, darkTip, darkAnchor, 3.0, darkToolbar, toolbarRegion);
        SaveVisual(darkTip, tooltipDark);

        Line("深色渲染后: IsDark=" + ThemeManager.IsDark + " ApplyCount=" + ThemeManager.ApplyCount);
        host.Close();

        Line("浅色全图: " + lightPng);
        Line("深色全图: " + darkPng);
        Line("浅色细节(2x): " + lightDetail);
        Line("深色细节(2x): " + darkDetail);
        Line("工具栏细节(3x) 浅色: " + lightToolbar);
        Line("工具栏细节(3x) 深色: " + darkToolbar);
        Line("输入框文字(3x): " + typedPng);
        Line("ToolTip 浅色: " + tooltipLight);
        Line("ToolTip 深色: " + tooltipDark);
        Line("渲染后 ThemeManager.IsDark: " + ThemeManager.IsDark);
        Line("AccentBrush（深色）: " + app.TryFindResource("AccentBrush"));
        Line("AccentBrush（浅色，切换验证）: " + SwapAndProbe(app));

        var reportPath = Path.Combine(temp, "custodian-theme-report.txt");
        File.WriteAllText(reportPath, Report.ToString(), Encoding.UTF8);
        Line("报告: " + reportPath);
        Line(problems == 0 ? "=== OK ===" : "=== 有问题: " + problems + " ===");

        return problems == 0 ? 0 : 2;
    }

    /// <summary>再切一次浅色并返回 AccentBrush，证明运行中切换生效；随后切回深色。</summary>
    private static string SwapAndProbe(Application app)
    {
        ThemeManager.SetDark(false);
        var light = app.TryFindResource("AccentBrush")?.ToString() ?? "(null)"
                    + ", AccentColor=" + app.TryFindResource("AccentColor");
        light = "AccentBrush=" + (app.TryFindResource("AccentBrush")?.ToString() ?? "(null)")
                + ", AccentColor=" + app.TryFindResource("AccentColor");
        ThemeManager.SetDark(true);
        return light;
    }

    private static (ResourceDictionary? Light, ResourceDictionary? Dark) FindVariantDictionaries(
        ResourceDictionary root)
    {
        var light = FindVariant(root, "Light")?.Dictionary;
        var dark = FindVariant(root, "Dark")?.Dictionary;
        return (light, dark);
    }

    private static (ResourceDictionary? Dictionary, ResourceDictionary? Parent)? FindVariant(
        ResourceDictionary root, string variant)
    {
        if (MatchesVariant(root, variant)) return (root, null);

        foreach (var child in root.MergedDictionaries)
        {
            if (MatchesVariant(child, variant)) return (child, root);

            var nested = FindVariant(child, variant);
            if (nested is not null) return (nested.Value.Dictionary, nested.Value.Parent ?? child);
        }

        return null;
    }

    /// <summary>只认字典自己的键：Contains 会连合并字典一起查，会把上层误判成变体字典。</summary>
    private static bool MatchesVariant(ResourceDictionary dictionary, string variant) =>
        dictionary.Keys.Cast<object>().Any(k => k is string s &&
            s == "ThemeVariant" && dictionary["ThemeVariant"] is string v &&
            string.Equals(v, variant, StringComparison.OrdinalIgnoreCase));

    private static string Dump(ResourceDictionary dictionary, int depth, HashSet<ResourceDictionary> seen)
    {
        var pad = new string(' ', depth * 2);
        var label = dictionary.Source?.OriginalString ?? "(inline)";
        var variant = dictionary.Keys.Cast<object>().Any(k => k is string s && s == "ThemeVariant")
            ? dictionary["ThemeVariant"] as string
            : "-";
        var cycle = seen.Contains(dictionary) ? "  <<< CYCLE" : string.Empty;
        var sb = new StringBuilder();
        sb.AppendLine(pad + label + "  keys=" + dictionary.Keys.Count + " variant=" + variant +
                      " merged=" + dictionary.MergedDictionaries.Count + cycle);
        if (depth > 6) return sb.ToString();
        seen.Add(dictionary);
        foreach (var child in dictionary.MergedDictionaries)
        {
            sb.Append(Dump(child, depth + 1, seen));
        }
        return sb.ToString();
    }

    /// <summary>静态检查 Fluent.xaml：所有 DynamicResource / StaticResource 引用的键都必须在 Fluent ∪ Light ∪ Dark 里定义。</summary>
    private static int CheckSelfContained()
    {
        var root = FindRepoRoot();
        if (root is null)
        {
            Line("[跳过] 找不到仓库根目录");
            return 0;
        }

        var themeDir = Path.Combine(root, "src", "Custodian.App", "Themes");
        var files = new[] { "Fluent.xaml", "Light.xaml", "Dark.xaml" }
            .Select(f => Path.Combine(themeDir, f))
            .ToArray();
        if (files.Any(f => !File.Exists(f)))
        {
            Line("[跳过] 主题源文件缺失");
            return 0;
        }

        var keyRegex = new Regex("x:Key=\"([^\"]+)\"");
        var refRegex = new Regex("\\{(?:DynamicResource|StaticResource)\\s+([A-Za-z_][\\w.]*)\\}");

        var defined = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            foreach (Match m in keyRegex.Matches(File.ReadAllText(file)))
            {
                defined.Add(m.Groups[1].Value);
            }
        }

        var missing = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            foreach (Match m in refRegex.Matches(File.ReadAllText(file)))
            {
                var key = m.Groups[1].Value;
                if (!defined.Contains(key)) missing.Add(key);
            }
        }

        if (missing.Count == 0)
        {
            Line("Fluent.xaml 内所有资源引用都能在 Fluent/Light/Dark 内解析（共 " + defined.Count + " 个键）。");
            return 0;
        }

        Line("[越界引用] " + string.Join(", ", missing));
        return missing.Count;
    }

    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Custodian.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }

        return null;
    }

    /// <summary>
    /// 把样例页放进一个真窗口里布局：只有连上 PresentationSource 的元素才会收到
    /// Application.Resources 的变更通知（离屏裸视觉树切主题不刷新，会得到假的阴性结果）。
    /// 窗口放到屏幕外并透明，不打扰用户。
    /// </summary>
    private static Window CreateHost(LabPage page)
    {
        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Left = -12000,
            Top = 0,
            Width = LogicalWidth,
            Height = LogicalHeight,
            Content = page,
        };

        window.Show();
        Settle(page, 350);
        return window;
    }

    private static void Settle(FrameworkElement page, int milliseconds = 220)
    {
        page.UpdateLayout();
        Pump(TimeSpan.FromMilliseconds(milliseconds));
        page.UpdateLayout();
    }

    private static void Pump(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = duration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    /// <summary>把 FluentToolTipStyle 的模板装在一个独立 ToolTip 上（放进可视树会被 WPF 拒绝），单独量好尺寸。</summary>
    private static ToolTip CreateToolTipVisual()
    {
        var tip = new ToolTip
        {
            Content = new TextBlock
            {
                Text = "Ctrl+Alt+Space 呼出面板；Tab 选择应用，Enter 启动。",
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 300,
            },
        };
        tip.SetResourceReference(FrameworkElement.StyleProperty, "FluentToolTipStyle");

        var size = new Size(380, 200);
        tip.Measure(size);
        tip.Arrange(new Rect(new Point(0, 0), tip.DesiredSize));
        tip.UpdateLayout();
        return tip;
    }

    private static void SaveVisual(FrameworkElement visual, string path)
    {
        var width = Math.Max(1, (int)Math.Ceiling(visual.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(visual.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    /// <summary>页面截图 + 叠加 ToolTip 样例（ToolTip 真机上是独立弹层，离屏截图只能这样合）。</summary>
    private static void SaveComposite(FrameworkElement page, ToolTip tip, Point anchor, double scale,
        string path, Rect? crop = null)
    {
        var width = (int)Math.Ceiling(LogicalWidth * scale);
        var height = (int)Math.Ceiling(LogicalHeight * scale);

        // 页面按 scale 倍渲染成位图（像素 = DIP * scale）
        var pageBitmap = new RenderTargetBitmap(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        pageBitmap.Render(page);

        var tipWidthDips = Math.Max(1, (int)Math.Ceiling(tip.ActualWidth));
        var tipHeightDips = Math.Max(1, (int)Math.Ceiling(tip.ActualHeight));
        var tipBitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(tipWidthDips * scale), (int)Math.Ceiling(tipHeightDips * scale),
            96 * scale, 96 * scale, PixelFormats.Pbgra32);
        tipBitmap.Render(tip);

        var left = Math.Min(anchor.X - tipWidthDips / 2.0, LogicalWidth - 16 - tipWidthDips);
        left = Math.Max(16, left);
        var top = anchor.Y;

        // DrawingVisual 的坐标是 DIP，最终位图按 dpi 自动放大 —— 这里不要再乘 scale
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(pageBitmap, new Rect(0, 0, LogicalWidth, LogicalHeight));
            dc.DrawImage(tipBitmap, new Rect(left, top, tipWidthDips, tipHeightDips));
        }

        var composited = new RenderTargetBitmap(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        composited.Render(visual);

        BitmapSource output = composited;
        if (crop is { } region)
        {
            var x = (int)Math.Round(region.X * scale);
            var y = (int)Math.Round(region.Y * scale);
            var w = Math.Min((int)Math.Round(region.Width * scale), width - x);
            var h = Math.Min((int)Math.Round(region.Height * scale), height - y);
            output = new CroppedBitmap(composited, new Int32Rect(x, y, w, h));
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(output));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    /// <summary>
    /// 真机验证 FluentToolTipStyle：开一个小窗口，程序化打开 ToolTip，停留几秒让外面截图。
    /// （ToolTip 是独立弹层，RenderTargetBitmap 抓不到，只能用屏幕截图验证。）
    /// </summary>
    private static void RunTooltipProbe(Application app)
    {
        var button = new Button();
        button.SetResourceReference(FrameworkElement.StyleProperty, "IconButtonStyle");
        button.Width = 32;
        button.Height = 32;
        button.Content = new TextBlock
        {
            Text = "?",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)app.FindResource("IconMonoBrush"),
        };

        var tip = new ToolTip
        {
            Content = new TextBlock
            {
                Text = "Ctrl+Alt+Space 呼出面板；Tab 选择应用，Enter 启动。",
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 300,
            },
            PlacementTarget = button,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
        };
        tip.SetResourceReference(FrameworkElement.StyleProperty, "FluentToolTipStyle");
        button.ToolTip = tip;

        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(button);

        var window = new Window
        {
            Title = "Custodian ToolTip probe",
            Content = panel,
            Width = 420,
            Height = 220,
            Left = 120,
            Top = 120,
            WindowStartupLocation = WindowStartupLocation.Manual,
        };

        window.Show();

        var openTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(500) };
        openTimer.Tick += (_, _) =>
        {
            openTimer.Stop();
            tip.IsOpen = true;
            Console.WriteLine("tooltip opened at " + DateTime.Now.ToString("HH:mm:ss.fff") + " IsOpen=" + tip.IsOpen);
        };
        openTimer.Start();

        var closeTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromSeconds(4) };
        closeTimer.Tick += (_, _) =>
        {
            closeTimer.Stop();
            window.Close();
            app.Shutdown();
        };
        closeTimer.Start();

        app.Run();
    }

    private static void RunLive(Application app)
    {
        var page = new LabPage();
        page.ApplyChrome(ThemeManager.IsDark);

        var window = new Window
        {
            Title = "Custodian ThemeLab  (Space 切换深浅色 / Esc 退出)",
            Content = page,
            Width = 1240,
            Height = 880,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = Brushes.Black,
        };

        ThemeManager.Changed += (_, _) => page.ApplyChrome(ThemeManager.IsDark);
        window.KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Space:
                    ThemeManager.SetDark(!ThemeManager.IsDark);
                    e.Handled = true;
                    break;
                case Key.Escape:
                    window.Close();
                    break;
            }
        };

        app.MainWindow = window;
        window.Show();
        app.Run();
    }
}