using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Custodian.App.Debugging;
using Custodian.App.Services;
using Custodian.App.ViewModels;

namespace Custodian.App;

/// <summary>进程内调试命令（仅 --debug / CUSTODIAN_DEBUG=1 时可达）。</summary>
public partial class MainWindow
{
    private static readonly JsonSerializerOptions DebugJson = new() { WriteIndented = false };

    private DebugServer? _debugServer;

    private void StartDebugServerIfEnabled()
    {
        if (!DebugServer.IsEnabled)
        {
            return;
        }

        _debugServer = new DebugServer(HandleDebugCommandLine);
        _debugServer.Start();

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DebugServer.DiscoveryPath)!);
            File.WriteAllText(DebugServer.DiscoveryPath, JsonSerializer.Serialize(new
            {
                pipe = DebugServer.PipeName,
                pid = Environment.ProcessId,
            }));
        }
        catch
        {
            // 发现文件写不了也不影响管道
        }

        Diagnostics.Write("debug: server started, pipe=" + DebugServer.PipeName);
    }

    private DebugResponse HandleDebugCommandLine(string line)
    {
        var space = line.IndexOf(' ');
        var verb = (space < 0 ? line : line[..space]).Trim().ToLowerInvariant();
        var argument = space < 0 ? string.Empty : line[(space + 1)..].Trim();

        return Dispatcher.CheckAccess()
            ? ExecuteDebugVerb(verb, argument)
            : Dispatcher.Invoke(() => ExecuteDebugVerb(verb, argument));
    }

    private DebugResponse ExecuteDebugVerb(string verb, string argument) => verb switch
    {
        "ping" => DebugResponse.Success(JsonSerializer.Serialize(new { pong = true, pid = Environment.ProcessId })),
        "state" => DebugResponse.Success(JsonSerializer.Serialize(BuildDebugState())),
        "rail" => DebugRail(argument),
        "rail-state" => DebugResponse.Success(JsonSerializer.Serialize(BuildRailState())),
        "filter" => DebugFilter(argument),
        "select" => DebugSelect(argument),
        "click" => DebugClick(argument),
        "clickat" => DebugClickAt(argument),
        "press" => DebugPress(argument),
        "hide" => DebugHide(),
        "deactivate" => DebugDeactivate(),
        "show" => DebugHotkey(),
        "hotkey" => DebugHotkey(),
        "minimize" => DebugWindowState(WindowState.Minimized),
        "refresh" => DebugRefresh(),
        "snapshot" => DebugSnapshot(argument),
        "perf" => DebugPerf(argument),
        "icons" => DebugIcons(argument),
        "standard" => DebugStandard(argument),
        "view" => DebugView(argument),
        "metrics" => DebugResponse.Success(JsonSerializer.Serialize(BuildMetrics())),
        "mem" => DebugResponse.Success(JsonSerializer.Serialize(BuildMemory())),
        "backdrop" => DebugResponse.Success(JsonSerializer.Serialize(DescribeBackdrop())),
        "backdropon" => ToggleBackdrop(true),
        "backdropoff" => ToggleBackdrop(false),
        "backdropsave" => DebugResponse.Success(SaveBackdrop(argument)),
        "procs" => DebugResponse.Success(JsonSerializer.Serialize(BuildProcessProbe())),
        "bounds" => DebugBounds(argument),
        "nav" => DebugResponse.Success(JsonSerializer.Serialize(new { rows = _vm.DescribeRows() })),
        "folder" => DebugFolder(argument),
        "focus" => DebugResponse.Success(JsonSerializer.Serialize(new
        {
            active = IsActive,
            focusedElement = Keyboard.FocusedElement?.GetType().Name,
            focusedName = (Keyboard.FocusedElement as FrameworkElement)?.Name,
            focusWithin = IsKeyboardFocusWithin,
            childFocusable = CountFocusableChildren(RootBorder),
            focusables = DescribeFocusable(RootBorder),
        })),
        "anim" => DebugAnim(argument),
        "settings" => DebugSettings(argument),
        "settingshot" => DebugSettingsShot(argument),
        "navraw" => DebugResponse.Success(JsonSerializer.Serialize(new
        {
            raw = _navRaw.Select(r => new { name = r.Name, x = r.X, y = r.Y }).ToList(),
            blocks = _vm.Sections.Where(s => s.IsVisible).Select(s => new { name = s.Name, width = Math.Round(s.BlockWidth, 1) }),
            passes = _navPasses,
            found = _navFound,
            root = _navRoot,
            buttons = _navButtons,
            visibleButtons = _navVisibleButtons,
            open = _vm.IsFolderOpen,
        })),
        "hittest" => DebugHitTest(argument),
        "tray" => DebugTrayState(),
        "tray-menu" => DebugTrayMenu(),
        "tray-exit" => DebugTrayExit(),
        "quit" => DebugQuit(),
        _ => DebugResponse.Fail("unknown verb: " + verb),
    };

    private object BuildDebugState() => new
    {
        pid = Environment.ProcessId,
        hwnd = _hwnd.ToInt64(),
        windowState = WindowState.ToString(),
        visible = IsVisible,
        tray = _trayIcon is { Visible: true },
        mica = _micaApplied,
        theme = _isDarkTheme ? "dark" : "light",
        view = _vm.ViewMode,
        busy = _vm.IsBusy,
        hotkey = _effectiveHotkey,
        filter = _vm.FilterText,
        rail = _vm.SelectedRail?.Name,
        selected = _vm.SelectedLabel,
        selectedKind = _vm.SelectedItem is null
            ? null
            : _vm.IsFolderSelected
                ? "folder"
                : _vm.IsStandardHeaderSelected ? "standard-header" : "card",
        folderOpen = _vm.IsFolderOpen,
        activeFolder = _vm.ActiveFolderTitle,
        columns = _vm.Columns,
        visibleCards = _vm.EnumerateVisibleCards().Count,
        sections = _vm.Sections.Select(s => new
        {
            name = s.Name,
            visible = s.IsVisible,
            apps = s.AllApps.Count,
            visibleApps = s.VisibleAppCount,
            standardApps = s.StandardApps.Count,
            standardExpanded = s.IsStandardExpanded,
            compact = s.IsCompactView,
            header = s.ShowHeaderRow,
            folderVisible = s.Folder.IsVisible,
            items = s.Items.Count,
            running = s.AllApps.Concat(s.StandardApps).Where(c => c.IsRunning).Select(c => c.Name).ToList(),
        }),
        rails = _vm.RailItems.Select(r => new { name = r.Name, count = r.CountText, selected = r.IsSelected }),
        running = _vm.Sections.SelectMany(s => s.AllApps.Concat(s.StandardApps))
            .Where(c => c.IsRunning)
            .Select(c => new { box = c.App.SandboxName, name = c.Name })
            .ToList(),
    };

    private object BuildRailState() => new
    {
        selected = _vm.SelectedRail?.Name,
        rails = _vm.RailItems.Select(r => new { name = r.Name, selected = r.IsSelected, count = r.CountText }),
        visibleCards = _vm.EnumerateVisibleCards().Count,
    };

    /// <summary>对比 NtQuerySystemInformation 快照与 Process.GetProcesses 的一致性 + 耗时。</summary>
    private object BuildProcessProbe()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var fast = Custodian.Interop.SystemProcesses.Snapshot();
        sw.Stop();
        var fastMs = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        var fastAgain = Custodian.Interop.SystemProcesses.Snapshot();
        sw.Stop();
        var fastReuseMs = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        var slow = System.Diagnostics.Process.GetProcesses()
            .Where(p =>
            {
                using (p)
                {
                    return true;
                }
            })
            .Count();
        sw.Stop();
        var slowMs = sw.Elapsed.TotalMilliseconds;

        var fastNames = fast.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var longNames = fast
            .Where(p => p.Name.Length >= 15)
            .Select(p => p.Name)
            .OrderBy(n => n)
            .Take(12)
            .ToList();

        return new
        {
            fastCount = fast.Count,
            fastAgainCount = fastAgain.Count,
            fastMs = Math.Round(fastMs, 2),
            fastReuseMs = Math.Round(fastReuseMs, 2),
            processCount = slow,
            getProcessesMs = Math.Round(slowMs, 2),
            longNames,
            sample = fast.Take(5).Select(p => p.Pid + ":" + p.Name),
        };
    }

    /// <summary>内存 / GC / 句柄快照，用于性能监控。</summary>
    private object BuildMemory()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        var gc = GC.GetGCMemoryInfo();

        int gdi = -1;
        int user = -1;
        try
        {
            gdi = Custodian.Interop.GdiResources.GetGdiObjectCount(process.Id);
            user = Custodian.Interop.GdiResources.GetUserObjectCount(process.Id);
        }
        catch
        {
            // 取不到就给 -1
        }

        return new
        {
            workingSetMb = Math.Round(process.WorkingSet64 / 1048576.0, 1),
            privateMb = Math.Round(process.PrivateMemorySize64 / 1048576.0, 1),
            managedMb = Math.Round(GC.GetTotalMemory(false) / 1048576.0, 1),
            heapSizeMb = Math.Round(gc.HeapSizeBytes / 1048576.0, 1),
            fragmentedMb = Math.Round(gc.FragmentedBytes / 1048576.0, 1),
            gen0 = GC.CollectionCount(0),
            gen1 = GC.CollectionCount(1),
            gen2 = GC.CollectionCount(2),
            framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            serverGc = System.Runtime.GCSettings.IsServerGC,
            captures = Custodian.App.Services.ProcessMonitor.CaptureCount,
            lastCaptureMs = Math.Round(Custodian.App.Services.ProcessMonitor.LastCaptureMs, 2),
            threads = process.Threads.Count,
            handles = process.HandleCount,
            renderTier = System.Windows.Media.RenderCapability.Tier >> 16,
            folderTiming = new
            {
                backdropMs = Math.Round(MainWindow.LastFolderTiming.Backdrop, 2),
                layoutMs = Math.Round(MainWindow.LastFolderTiming.Layout, 2),
                viewportMs = Math.Round(MainWindow.LastFolderTiming.Viewport, 2),
            },
            gdi,
            user,
        };
    }

    private object BuildMetrics()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var topLeft = PointToScreen(new Point(0, 0));
        return new
        {
            screenLeft = topLeft.X,
            screenTop = topLeft.Y,
            dipWidth = ActualWidth,
            dipHeight = ActualHeight,
            scaleX = dpi.DpiScaleX,
            scaleY = dpi.DpiScaleY,
            railTop = RailScroller.TranslatePoint(new Point(0, 0), this).Y,
            railWidth = RailScroller.ActualWidth,
            railHeight = RailScroller.ActualHeight,
            columns = _vm.Columns,
            measuredColumns = _gridPassColumns,
            observedInRow = _gridPassObserved,
            panelWidth = Math.Round(_gridPassPanelWidth, 2),
            cardStep = Math.Round(_gridPassStep, 2),
            rowPitch = Math.Round(_gridPassRowPitch, 2),
            gridPasses = _gridPassCount,
            viewportWidth = Math.Round(CardsScroller.ViewportWidth, 2),
            viewportHeight = Math.Round(CardsScroller.ViewportHeight, 2),
        };
    }

    /// <summary>列出当前窗口里还可聚焦的子元素（调试用；正常应为空）。</summary>
    private static System.Collections.Generic.List<string> DescribeFocusable(DependencyObject root)
    {
        var result = new System.Collections.Generic.List<string>();
        var stack = new System.Collections.Generic.Stack<DependencyObject>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current is UIElement element && element.Focusable)
            {
                var name = (element as FrameworkElement)?.Name;
                result.Add(element.GetType().Name + (string.IsNullOrEmpty(name) ? string.Empty : "#" + name));
            }

            var children = System.Windows.Media.VisualTreeHelper.GetChildrenCount(current);
            for (var i = 0; i < children; i++)
            {
                stack.Push(System.Windows.Media.VisualTreeHelper.GetChild(current, i));
            }
        }

        result.Sort(StringComparer.Ordinal);
        return result;
    }

    /// <summary>统计当前窗口里还有多少个可聚焦子元素（应当为 0）。</summary>
    private static int CountFocusableChildren(DependencyObject root)
    {
        var count = 0;
        var stack = new System.Collections.Generic.Stack<DependencyObject>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current is UIElement element && element is not Window && element.Focusable && element is not System.Windows.Controls.Primitives.ScrollBar)
            {
                count++;
            }

            var children = System.Windows.Media.VisualTreeHelper.GetChildrenCount(current);
            for (var i = 0; i < children; i++)
            {
                stack.Push(System.Windows.Media.VisualTreeHelper.GetChild(current, i));
            }
        }

        return count;
    }

    private readonly System.Collections.Generic.List<object> _animTrace = new();
    private System.Windows.Threading.DispatcherTimer? _animTraceTimer;
    private DateTime _animTraceStart;

    private void StartAnimationTrace()
    {
        _animTrace.Clear();
        _animTraceStart = DateTime.UtcNow;
        _animTraceTimer ??= new System.Windows.Threading.DispatcherTimer(
            System.Windows.Threading.DispatcherPriority.Render, Dispatcher);
        _animTraceTimer.Interval = TimeSpan.FromMilliseconds(25);
        _animTraceTimer.Tick -= OnAnimTraceTick;
        _animTraceTimer.Tick += OnAnimTraceTick;
        _animTraceTimer.Start();
    }

    private void OnAnimTraceTick(object? sender, EventArgs e)
    {
        var elapsed = (DateTime.UtcNow - _animTraceStart).TotalMilliseconds;
        _animTrace.Add(new
        {
            t = Math.Round(elapsed),
            root = Math.Round(RootBorder.Opacity, 2),
            shift = Math.Round(RootShift.Y, 1),
            overlay = FolderOverlay.Visibility == Visibility.Visible
                ? Math.Round(FolderPanelHost.Opacity, 2)
                : -1,
            scrim = Math.Round(FolderScrim.Opacity, 2),
            scale = Math.Round(FolderPanelScale.ScaleX, 3),
            ring = LaunchRing.Visibility == Visibility.Visible
                ? Math.Round(LaunchRing.Opacity, 2)
                : -1,
            offset = Math.Round(LaunchRing.StrokeDashOffset, 0),
            visible = IsVisible,
        });

        if (elapsed > 700)
        {
            _animTraceTimer?.Stop();
        }
    }

    /// <summary>
    /// anim                查看动画状态（覆盖层 / 边框 loading / 窗口可见性）
    /// anim trace          开始按 25ms 采样动画过程（之后用 anim 读取 trace）
    /// anim intro|outro    播窗口出现 / 收起动画
    /// anim ring           播启动应用的边框 loading（不真的启动程序）
    /// </summary>
    private DebugResponse DebugAnim(string argument)
    {
        var text = argument.Trim();
        if (text.EndsWith(" --traced", StringComparison.Ordinal))
        {
            text = text[..^" --traced".Length].Trim();
            StartAnimationTrace();
        }

        var split = text.IndexOf(' ');
        var scene = (split < 0 ? text : text[..split]).Trim().ToLowerInvariant();
        var rest = split < 0 ? string.Empty : text[(split + 1)..].Trim();

        if (scene == "start")
        {
            // anim start <scene>：先开采样，再在同一帧里跑场景，采样才抓得到
            return DebugAnim(rest + " --traced");
        }

        switch (scene)
        {
            case "trace":
                StartAnimationTrace();
                break;

            case "start":
                DebugAnim(argument[(argument.IndexOf(' ') + 1)..].Trim() + " --traced");
                break;

            case "intro":
                _hiding = false;
                if (!IsVisible)
                {
                    Show();
                }

                PlayWindowIntro();
                break;

            case "outro":
                HidePanel();
                break;

            case "ring":
                _ = PlayLaunchRingAsync();
                break;

            case "folder-open":
                var section = _vm.Sections.FirstOrDefault(s => s.StandardApps.Count > 0);
                if (section is not null)
                {
                    _vm.OpenFolder(section.Folder);
                    UpdateLayout();
                    RunViewportPassNow();
                }

                break;

            case "folder-close":
                _vm.CloseFolder();
                break;

            case "introstill":
                // 播入场动画并在 ~90ms 时抓一帧（检查动画期间没有焦点框）
                if (!IsVisible)
                {
                    Show();
                }

                _hiding = false;
                PlayWindowIntro();
                var introPath = rest.Length > 0 ? rest : "intro.png";
                var introTimer = new System.Windows.Threading.DispatcherTimer(
                    System.Windows.Threading.DispatcherPriority.Render, Dispatcher)
                {
                    Interval = TimeSpan.FromMilliseconds(90),
                };
                introTimer.Tick += (_, _) =>
                {
                    introTimer.Stop();
                    DebugSnapshot(introPath);
                };
                introTimer.Start();
                break;

            case "ringstill":
                // 播边框 loading，并在 ~180ms 时截一张图（用来留档动画效果）
                _ = PlayLaunchRingAsync();
                var path = rest.Length > 0 ? rest : "ring.png";
                var timer = new System.Windows.Threading.DispatcherTimer(
                    System.Windows.Threading.DispatcherPriority.Render, Dispatcher)
                {
                    Interval = TimeSpan.FromMilliseconds(180),
                };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    DebugSnapshot(path);
                };
                timer.Start();
                break;

            case "launch":
                // 走真实启动链路：AppLaunched → 边框 loading → HideRequested → 收起动画
                if (_vm.SelectedCard is { } card)
                {
                    _vm.Launch(card);
                }

                break;
        }

        return DebugResponse.Success(JsonSerializer.Serialize(new
        {
            animations = System.Windows.SystemParameters.ClientAreaAnimation && !SafeMode.Has("anim"),
            visible = IsVisible,
            hiding = _hiding,
            overlay = FolderOverlay.Visibility.ToString(),
            scrimOpacity = Math.Round(FolderScrim.Opacity, 2),
            snapshotOpacity = Math.Round(BackdropSnapshot.Opacity, 2),
            panelOpacity = Math.Round(FolderPanelHost.Opacity, 2),
            panelScale = Math.Round(FolderPanelScale.ScaleX, 3),
            ring = LaunchRing.Visibility.ToString(),
            ringOpacity = Math.Round(LaunchRing.Opacity, 2),
            ringOffset = Math.Round(LaunchRing.StrokeDashOffset, 1),
            rootOpacity = Math.Round(RootBorder.Opacity, 2),
            rootShiftY = Math.Round(RootShift.Y, 1),
            folderOpen = _vm.IsFolderOpen,
            trace = _animTrace,
        }));
    }

    /// <summary>
    /// settings               查看当前设置与存储路径
    /// settings open|close     打开/关闭设置窗口
    /// settings theme &lt;system|light|dark&gt;
    /// settings view &lt;standard|compact&gt;
    /// settings sandbox &lt;路径|default&gt;
    /// </summary>
    private DebugResponse DebugSettings(string argument)
    {
        var space = argument.IndexOf(' ');
        var command = (space < 0 ? argument : argument[..space]).Trim().ToLowerInvariant();
        var value = space < 0 ? string.Empty : argument[(space + 1)..].Trim();

        switch (command)
        {
            case "open":
                OpenSettings();
                UpdateLayout();
                break;

            case "close":
                CloseSettings();
                break;

            case "tab":
                OpenSettings();
                UpdateLayout();
                _settingsWindow?.SelectTab(value);
                break;

            case "theme":
                if (value.Length > 0)
                {
                    _settingsCoordinator.SetTheme(value);
                    _settingsWindow?.RefreshFromSettings();
                }

                break;

            case "view":
                if (value.Length > 0)
                {
                    _settingsCoordinator.SetCompact(string.Equals(value, "compact", StringComparison.OrdinalIgnoreCase));
                    _settingsWindow?.RefreshFromSettings();
                }

                break;

            case "sandbox":
                var root = value.Length == 0 || string.Equals(value, "default", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : value;
                _ = _settingsCoordinator.SetSandboxRootAsync(root);
                break;
        }

        return DebugResponse.Success(JsonSerializer.Serialize(BuildSettingsState()));
    }

    private object BuildSettingsState()
    {
        var settings = _vm.Settings;
        return new
        {
            profile = Custodian.Core.Settings.StoragePaths.Profile.ToString(),
            readPath = Custodian.Core.Settings.StoragePaths.ReadSettingsPath,
            writePath = Custodian.Core.Settings.StoragePaths.WriteSettingsPath,
            theme = Custodian.Core.Settings.CustodianSettings.NormalizeTheme(settings.Theme),
            isDark = Themes.ThemeManager.IsDark,
            followSystem = Themes.ThemeManager.FollowSystem,
            cardView = _vm.IsCompactView ? "compact" : "standard",
            sandboxRoot = settings.SandboxRootPath,
            effectiveRoot = _vm.SandboxRootTemplate,
            rootSource = _vm.SandboxRootSource,
            sandboxIniPath = _vm.SandboxIniPath,
            windowOpen = IsSettingsOpen,
        };
    }

    private DebugResponse DebugSettingsShot(string argument)
    {
        if (_settingsWindow is null)
        {
            return DebugResponse.Fail("settings window is not open");
        }

        try
        {
            var root = (FrameworkElement)_settingsWindow.Content;
            var width = (int)Math.Ceiling(root.ActualWidth);
            var height = (int)Math.Ceiling(root.ActualHeight);
            if (width <= 0 || height <= 0)
            {
                return DebugResponse.Fail("settings window has no size");
            }

            var background = TryFindResource("BackdropBaseBrush") as Brush ?? Brushes.Black;
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.DrawRectangle(background, null, new Rect(0, 0, width, height));
                context.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, width, height));
            }

            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(argument);
            encoder.Save(stream);

            return DebugResponse.Success(argument + " " + width + "x" + height);
        }
        catch (Exception exception)
        {
            return DebugResponse.Fail(exception.Message);
        }
    }

    private DebugResponse ToggleBackdrop(bool enabled)
    {
        SetBackdropEnabled(enabled);
        return DebugResponse.Success(enabled ? "on" : "off");
    }

    /// <summary>folder &lt;沙盒名&gt;：打开该沙盒的「标准应用程序」文件夹弹窗（回归用）。</summary>
    private DebugResponse DebugFolder(string argument)
    {
        var section = _vm.Sections.FirstOrDefault(
            s => s.Name.Contains(argument, StringComparison.CurrentCultureIgnoreCase));

        if (section is null)
        {
            return DebugResponse.Fail("section not found: " + argument);
        }

        _vm.OpenFolder(section.Folder);
        UpdateLayout();
        RunViewportPassNow();

        return DebugResponse.Success(JsonSerializer.Serialize(new
        {
            section = section.Name,
            open = _vm.IsFolderOpen,
            apps = _vm.ActiveFolderApps.Count,
            rows = _vm.DescribeRows().Count,
        }));
    }

    private DebugResponse DebugRail(string argument)
    {
        RailItemViewModel? item;
        if (argument.Length == 0 || argument.Equals("all", StringComparison.OrdinalIgnoreCase) || argument == "0")
        {
            item = _vm.RailItems.FirstOrDefault();
        }
        else if (int.TryParse(argument, out var index))
        {
            item = index >= 0 && index < _vm.RailItems.Count ? _vm.RailItems[index] : null;
        }
        else
        {
            item = _vm.RailItems.FirstOrDefault(r => r.Name.Contains(argument, StringComparison.CurrentCultureIgnoreCase));
        }

        if (item is null)
        {
            return DebugResponse.Fail("rail not found: " + argument);
        }

        _vm.SelectRail(item);
        return DebugResponse.Success(JsonSerializer.Serialize(new { rail = item.Name, cards = _vm.EnumerateVisibleCards().Count }));
    }

    private DebugResponse DebugFilter(string argument)
    {
        FilterBox.Text = argument;
        return DebugResponse.Success(JsonSerializer.Serialize(new
        {
            filter = _vm.FilterText,
            matched = _vm.EnumerateVisibleCards().Count,
        }));
    }

    private DebugResponse DebugSelect(string argument)
    {
        if (!_vm.TrySelect(argument, out var card))
        {
            return DebugResponse.Fail("card not found: " + argument);
        }

        return DebugResponse.Success(JsonSerializer.Serialize(new { selected = card?.Name, index = _vm.EnumerateVisibleCards().ToList().IndexOf(card!) }));
    }

    private DebugResponse DebugClick(string argument)
    {
        if (!_vm.TrySelect(argument, out var card) || card is null)
        {
            return DebugResponse.Fail("card not found: " + argument);
        }

        _vm.Launch(card);
        return DebugResponse.Success(JsonSerializer.Serialize(new
        {
            clicked = card.Name,
            box = card.App.SandboxName,
            target = card.App.TargetPath,
        }));
    }

    /// <summary>应用内模拟点击：命中链上找最近的按钮并直接抛 Click（不经过系统输入、不动鼠标）。</summary>
    private DebugResponse DebugClickAt(string argument)
    {
        var parts = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 ||
            !double.TryParse(parts[0], out var screenX) ||
            !double.TryParse(parts[1], out var screenY))
        {
            return DebugResponse.Fail("usage: clickat <screenX> <screenY>");
        }

        var point = PointFromScreen(new Point(screenX, screenY));
        var hit = VisualTreeHelper.HitTest(RootBorder, point);
        if (hit?.VisualHit is not UIElement target)
        {
            return DebugResponse.Fail("no hit target");
        }

        var button = FindAncestor<ButtonBase>(target);
        if (button is null)
        {
            return DebugResponse.Fail("no button at point");
        }

        if (button is ToggleButton toggle)
        {
            toggle.IsChecked = !(toggle.IsChecked == true);
        }

        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
        return DebugResponse.Success(JsonSerializer.Serialize(new
        {
            target = target.GetType().Name,
            control = button.GetType().Name,
            rail = _vm.SelectedRail?.Name,
            selected = _vm.SelectedCard?.Name,
        }));
    }

    /// <summary>应用内模拟按键：抛真实 PreviewKeyDown。</summary>
    private DebugResponse DebugPress(string argument)
    {
        if (!Enum.TryParse<Key>(argument, true, out var key))
        {
            return DebugResponse.Fail("unknown key: " + argument);
        }

        var source = PresentationSource.FromVisual(this);
        if (source is null)
        {
            return DebugResponse.Fail("no presentation source");
        }

        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        };

        RaiseEvent(args);
        return DebugResponse.Success(JsonSerializer.Serialize(new
        {
            key = key.ToString(),
            handled = args.Handled,
            visible = IsVisible,
            selected = _vm.SelectedCard?.Name,
        }));
    }

    private DebugResponse DebugHide()
    {
        HidePanel();
        return DebugResponse.Success(JsonSerializer.Serialize(new { visible = IsVisible, tray = _trayIcon is { Visible: true } }));
    }

    /// <summary>模拟一次失焦（走真实的 OnWindowDeactivated 处理）。</summary>
    private DebugResponse DebugDeactivate()
    {
        _hasBeenActivated = true;
        OnWindowDeactivated(this, EventArgs.Empty);
        return DebugResponse.Success(JsonSerializer.Serialize(new { visible = IsVisible }));
    }

    private DebugResponse DebugHotkey()
    {
        ShowAndFocus();
        return DebugResponse.Success(JsonSerializer.Serialize(new { windowState = WindowState.ToString(), visible = IsVisible }));
    }

    private DebugResponse DebugWindowState(WindowState state)
    {
        WindowState = state;
        return DebugResponse.Success(JsonSerializer.Serialize(new { windowState = WindowState.ToString() }));
    }

    private DebugResponse DebugRefresh()
    {
        _ = ReloadAsync();
        return DebugResponse.Success(JsonSerializer.Serialize(new { refreshing = true }));
    }

    /// <summary>性能测量：perf rail:&lt;名称&gt; / perf filter:&lt;文本&gt;。</summary>
    private DebugResponse DebugPerf(string argument)
    {
        var colon = argument.IndexOf(':');
        var what = colon < 0 ? argument : argument[..colon];
        var value = colon < 0 ? string.Empty : argument[(colon + 1)..];

        var stopwatch = Stopwatch.StartNew();
        double vmMs;
        switch (what.ToLowerInvariant())
        {
            case "rail":
                var rail = value.Equals("all", StringComparison.OrdinalIgnoreCase) || value.Length == 0
                    ? _vm.RailItems.FirstOrDefault()
                    : _vm.RailItems.FirstOrDefault(r => r.Name.Contains(value, StringComparison.CurrentCultureIgnoreCase));
                if (rail is null)
                {
                    return DebugResponse.Fail("rail not found");
                }

                _vm.SelectRail(rail);
                break;

            case "filter":
                FilterBox.Text = value;
                break;

            default:
                return DebugResponse.Fail("usage: perf rail:<name> | perf filter:<text>");
        }

        vmMs = stopwatch.Elapsed.TotalMilliseconds;
        UpdateLayout();
        stopwatch.Stop();

        return DebugResponse.Success(JsonSerializer.Serialize(new
        {
            operation = argument,
            vmMs,
            layoutMs = stopwatch.Elapsed.TotalMilliseconds - vmMs,
            elapsedMs = stopwatch.Elapsed.TotalMilliseconds,
            visibleCards = _vm.EnumerateVisibleCards().Count,
        }));
    }

    /// <summary>
    /// 图标懒加载统计。用法：<c>icons</c>（只读快照）/ <c>icons reset</c>（清缓存+统计，便于重复测量）/
    /// <c>icons scan</c>（立即触发一次视口重算）。
    /// </summary>
    private DebugResponse DebugIcons(string argument)
    {
        var action = argument.Trim().ToLowerInvariant();
        var reset = action is "reset" or "clear";

        if (reset)
        {
            ResetIconRequests();
            _vm.Icons.ResetStatistics(clearCache: true, dropQueue: true);
        }

        if (reset || action == "scan")
        {
            ScheduleIconPass();
        }

        var stats = _vm.Icons.Snapshot();
        var cards = _vm.Sections.SelectMany(s => s.AllApps.Concat(s.StandardApps)).ToList();

        return DebugResponse.Success(JsonSerializer.Serialize(new
        {
            busy = _vm.IsBusy,
            cards = cards.Count,
            visibleCards = _vm.EnumerateVisibleCards().Count,
            cardsWithIcon = cards.Count(c => c.HasIcon),
            cached = _vm.Icons.CachedCount,
            requested = stats.Requested,
            cacheHits = stats.CacheHits,
            deduped = stats.Deduped,
            queued = stats.Queued,
            loaded = stats.Loaded,
            failed = stats.Failed,
            delivered = stats.Delivered,
            pending = stats.Pending,
            queueLength = stats.QueueLength,
            inFlight = stats.InFlight,
            viewportRequests = stats.ViewportRequests,
            prefetchRequests = stats.PrefetchRequests,
            extractions = stats.Extractions,
            extractMs = Math.Round(stats.ExtractMs, 1),
            avgExtractMs = stats.Extractions > 0 ? Math.Round(stats.ExtractMs / stats.Extractions, 1) : 0,
            firstIconMs = Math.Round(stats.FirstIconMs, 1),
            firstIconRequests = stats.FirstIconRequests,
            elapsedMs = Math.Round(stats.ElapsedMs, 1),
            scrollY = Math.Round(CardsScroller.VerticalOffset, 1),
            viewportHeight = Math.Round(CardsScroller.ViewportHeight, 1),
            viewport = new
            {
                cards = _iconPassViewport,
                prefetch = _iconPassPrefetch,
                passes = _iconPassCount,
                lastPassMs = Math.Round(_iconPassMs, 2),
            },
        }));
    }

    /// <summary>展开/收起某分组的「标准应用程序」（按 _vm.Sections 下标），用于自动化验证卡片数量较大时的滚动加载。</summary>
    private DebugResponse DebugStandard(string argument)
    {
        if (!int.TryParse(argument, out var index) || index < 0 || index >= _vm.Sections.Count)
        {
            return DebugResponse.Fail("usage: standard <sectionIndex>");
        }

        var section = _vm.Sections[index];
        _vm.ToggleStandard(section);
        UpdateLayout();
        ScheduleIconPass();

        return DebugResponse.Success(JsonSerializer.Serialize(new
        {
            section = section.Name,
            expanded = section.IsStandardExpanded,
            visibleCards = _vm.EnumerateVisibleCards().Count,
        }));
    }

    /// <summary>
    /// 卡片视图：<c>view</c> 只读当前 / <c>view compact</c> / <c>view standard</c> / <c>view toggle</c>。
    /// 返回 <c>{"view":"compact","columns":8,"step":104}</c>；columns 是切换后按真实布局量出来的每行卡片数。
    /// </summary>
    private DebugResponse DebugView(string argument)
    {
        var stopwatch = Stopwatch.StartNew();
        switch (argument.Trim().ToLowerInvariant())
        {
            case "":
                break;

            case "compact":
                _vm.SetView(true);
                ApplyCardViewLayout();
                break;

            case "standard":
                _vm.SetView(false);
                ApplyCardViewLayout();
                break;

            case "toggle":
                _vm.ToggleView();
                ApplyCardViewLayout();
                break;

            default:
                return DebugResponse.Fail("usage: view [compact|standard|toggle]");
        }

        UpdateLayout();
        stopwatch.Stop();
        return DebugResponse.Success(JsonSerializer.Serialize(new
        {
            view = _vm.ViewMode,
            columns = _vm.Columns,
            step = _vm.Metrics.Step,
            elapsedMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2),
        }));
    }

    private DebugResponse DebugSnapshot(string argument)
    {
        var path = string.IsNullOrWhiteSpace(argument)
            ? Path.Combine(Path.GetTempPath(), "custodian-snapshot.png")
            : argument;

        try
        {
            var visual = (FrameworkElement)RootBorder;
            var dpi = VisualTreeHelper.GetDpi(visual);
            var widthDips = visual.ActualWidth;
            var heightDips = visual.ActualHeight;

            if (widthDips <= 0 || heightDips <= 0)
            {
                return DebugResponse.Fail("window has no size yet");
            }

            var width = (int)Math.Ceiling(widthDips * dpi.DpiScaleX);
            var height = (int)Math.Ceiling(heightDips * dpi.DpiScaleY);

            var background = new SolidColorBrush(_isDarkTheme
                ? Color.FromRgb(0x20, 0x20, 0x20)
                : Color.FromRgb(0xF3, 0xF3, 0xF3));

            var composed = new DrawingVisual();
            using (var context = composed.RenderOpen())
            {
                context.PushTransform(new ScaleTransform(dpi.DpiScaleX, dpi.DpiScaleY));
                context.DrawRectangle(background, null, new Rect(0, 0, widthDips, heightDips));
                context.DrawRectangle(new VisualBrush(visual), null, new Rect(0, 0, widthDips, heightDips));
                context.Pop();
            }

            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(composed);

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(path);
            encoder.Save(stream);

            return DebugResponse.Success(JsonSerializer.Serialize(new { path, width, height }));
        }
        catch (Exception ex)
        {
            return DebugResponse.Fail("snapshot failed: " + ex.Message);
        }
    }

    private DebugResponse DebugBounds(string argument)
    {
        if (argument.StartsWith("layout:", StringComparison.OrdinalIgnoreCase))
        {
            return DebugLayout(argument[7..]);
        }

        FrameworkElement? element = argument switch
        {
            "rail" => RailScroller,
            "close" => CloseButton,
            "refresh" => RefreshButton,
            "filter" => FilterBox,
            "cards" => CardsScroller,
            "toggle" => FindDescendant<Button>(GroupsHost, b => b.DataContext is SandboxSectionViewModel),
            _ => FindName(argument) as FrameworkElement,
        };

        if (element is null)
        {
            return DebugResponse.Fail("element not found: " + argument);
        }

        var topLeft = element.PointToScreen(new Point(0, 0));
        var bottomRight = element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight));
        return DebugResponse.Success(JsonSerializer.Serialize(new
        {
            name = argument,
            left = topLeft.X,
            top = topLeft.Y,
            width = bottomRight.X - topLeft.X,
            height = bottomRight.Y - topLeft.Y,
            centerX = (topLeft.X + bottomRight.X) / 2,
            centerY = (topLeft.Y + bottomRight.Y) / 2,
        }));
    }

    /// <summary>把某个元素的子树布局打出来（尺寸 / Margin / Padding / 相对偏移），排查对齐问题用。</summary>
    private DebugResponse DebugLayout(string name)
    {
        if (FindName(name) is not FrameworkElement root)
        {
            return DebugResponse.Fail("element not found: " + name);
        }

        var entries = new List<object>();
        var origin = root.PointToScreen(new Point(0, 0));

        void Walk(DependencyObject node, int depth)
        {
            if (depth > 6 || entries.Count > 120)
            {
                return;
            }

            if (node is FrameworkElement element)
            {
                var topLeft = element.PointToScreen(new Point(0, 0));
                entries.Add(new
                {
                    depth,
                    type = element.GetType().Name,
                    name = element.Name.Length > 0 ? element.Name : null,
                    x = Math.Round(topLeft.X - origin.X, 2),
                    y = Math.Round(topLeft.Y - origin.Y, 2),
                    w = Math.Round(element.ActualWidth, 2),
                    h = Math.Round(element.ActualHeight, 2),
                    margin = element.Margin.ToString(),
                    padding = element is Control control ? control.Padding.ToString() : null,
                    valign = element.VerticalAlignment.ToString(),
                    halign = element.HorizontalAlignment.ToString(),
                    vcontent = element is Control c2 ? c2.VerticalContentAlignment.ToString() : null,
                });
            }

            var count = VisualTreeHelper.GetChildrenCount(node);
            for (var i = 0; i < count; i++)
            {
                Walk(VisualTreeHelper.GetChild(node, i), depth + 1);
            }
        }

        Walk(root, 0);
        return DebugResponse.Success(JsonSerializer.Serialize(new { root = name, entries }));
    }

    private DebugResponse DebugHitTest(string argument)
    {
        var parts = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 ||
            !double.TryParse(parts[0], out var screenX) ||
            !double.TryParse(parts[1], out var screenY))
        {
            return DebugResponse.Fail("usage: hittest <screenX> <screenY>");
        }

        var point = PointFromScreen(new Point(screenX, screenY));
        var chain = new List<string>();
        var hit = VisualTreeHelper.HitTest(RootBorder, point);
        var current = hit?.VisualHit as DependencyObject;
        while (current is not null)
        {
            chain.Add(current is FrameworkElement element
                ? element.GetType().Name + "#" + (element.Name.Length > 0 ? element.Name : "-")
                : current.GetType().Name);
            current = VisualTreeHelper.GetParent(current);
        }

        return DebugResponse.Success(JsonSerializer.Serialize(new
        {
            screen = new { x = screenX, y = screenY },
            local = new { x = point.X, y = point.Y },
            chain,
        }));
    }

    private DebugResponse DebugTrayState() => DebugResponse.Success(JsonSerializer.Serialize(new
    {
        tray = _trayIcon is { Visible: true },
        tooltip = _trayIcon?.Text,
        menuItems = _trayMenu?.Items.Count ?? 0,
        menuVisible = _trayMenu?.Visible ?? false,
        visible = IsVisible,
    }));

    private DebugResponse DebugTrayMenu()
    {
        if (!IsVisible)
        {
            Show();
        }

        Activate();
        ShowTrayMenu();
        return DebugResponse.Success(JsonSerializer.Serialize(new { opened = _trayMenu?.Visible ?? false }));
    }

    private DebugResponse DebugTrayExit()
    {
        Dispatcher.BeginInvoke(new Action(ExitFromTray));
        return DebugResponse.Shutdown(JsonSerializer.Serialize(new { exiting = true }));
    }

    private DebugResponse DebugQuit()
    {
        Dispatcher.BeginInvoke(new Action(() => Application.Current.Shutdown()));
        return DebugResponse.Shutdown(JsonSerializer.Serialize(new { quitting = true }));
    }

    private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node is not null)
        {
            if (node is T match)
            {
                return match;
            }

            node = VisualTreeHelper.GetParent(node);
        }

        return null;
    }

    private static T? FindDescendant<T>(DependencyObject? root, Func<T, bool>? predicate = null) where T : DependencyObject
    {
        if (root is null)
        {
            return null;
        }

        if (root is T match && (predicate is null || predicate(match)))
        {
            return match;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var found = FindDescendant(VisualTreeHelper.GetChild(root, i), predicate);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }
}
