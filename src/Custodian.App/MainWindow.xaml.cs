using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Custodian.App.Services;
using Custodian.App.ViewModels;
using Custodian.Core.Discovery;
using Custodian.Core.Settings;
using Custodian.Interop;

namespace Custodian.App;

/// <summary>
/// 视图层：窗口能力（Mica / 热键 / 托盘 / 调试通道）与输入转发。
/// 面板数据与交互逻辑都在 <see cref="MainViewModel"/>。
/// </summary>
public partial class MainWindow : Window
{
    // 卡片网格的兜底值；真实值由 MeasureCardGrid() 从布局里量出来（DPI / 内边距 / 滚动条都不影响）
    private const double FallbackCardStep = 184;
    private const double FallbackRowPitch = 120;
    private const int HotkeyId = 0x43;

    /// <summary>视口重算节流间隔（滚动时最多每 70ms 算一次，避免刷爆 UI 线程）。</summary>
    private const int IconThrottleMs = 70;

    /// <summary>视口上下各多预取的「行」数（一行 = 实测行高 _rowPitch）。</summary>
    private const int IconPrefetchRows = 1;

    private readonly CustodianSettings _settings;
    private readonly StaWorker _sta;
    private readonly ProcessMonitor _processMonitor;
    private readonly MainViewModel _vm;

    private CancellationTokenSource? _scanCts;
    private HotkeyManager? _hotkeys;
    private IntPtr _hwnd;
    private bool _isDarkTheme;
    private bool _micaApplied;
    private bool _hasBeenActivated;
    private bool _suppressDeactivateHide;
    private DispatcherTimer? _hintTimer;
    private string _effectiveHotkey = "Ctrl+Alt+Space";

    // 图标懒加载：滚动/尺寸变化只标脏，由 _iconThrottle 合并成一次视口重算
    private readonly DispatcherTimer _iconThrottle;
    private readonly HashSet<AppCardViewModel> _iconRequested = new();
    private readonly Dictionary<string, string> _iconPathCache = new(StringComparer.OrdinalIgnoreCase);
    private bool _iconPassDirty;

    // 实测出来的卡片网格尺寸（列数 / 列步进 / 行步进，DIP）
    private double _cardStep = FallbackCardStep;
    private double _rowPitch = FallbackRowPitch;
    private int _gridPassColumns;
    private double _gridPassStep;
    private double _gridPassRowPitch;
    private int _gridPassObserved;
    private double _gridPassPanelWidth;
    private long _gridPassCount;
    private int _iconPassViewport;
    private int _iconPassPrefetch;
    private double _iconPassMs;
    private long _iconPassCount;

    public MainWindow()
    {
        _settings = CustodianSettings.Load();
        _sta = new StaWorker();
        var icons = new IconService(_sta);
        _vm = new MainViewModel(_settings, new ShortcutReaderAdapter(), icons, _sta);
        _processMonitor = new ProcessMonitor(() => _vm.CaptureRunning());

        _iconThrottle = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(IconThrottleMs),
        };
        _iconThrottle.Tick += OnIconThrottleTick;

        InitializeComponent();
        DataContext = _vm;

        _vm.HideRequested += (_, _) => HidePanel();
        _vm.HintRequested += (_, message) => ShowTransientHint(message);
        _vm.SelectionMoved += (_, item) => ScrollItemIntoView(item);
        _vm.FolderOpened += (_, _) => RefreshFolderLayer();
        _vm.FolderClosed += (_, _) => RefreshFolderLayer();
        _vm.LayoutInvalidated += (_, _) => ScheduleIconPass();

        // 内容变了（筛选/左栏/切视图/重扫）--> 背景快照失效
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsBusy) && !_vm.IsBusy)
            {
                MarkBackdropDirty();
            }
        };

        // 弹窗开着时窗口尺寸变了，快照要重截（拉伸会糊）
        SizeChanged += (_, _) =>
        {
            MarkBackdropDirty();
            QueueBackdropRefresh();
        };

        // 布局指标一变（标准↔紧凑视图）就标脏视口：列数测量与图标补图都在节流 tick 里跟着重跑。
        _vm.Metrics.PropertyChanged += (_, _) =>
        {
            MarkBackdropDirty();
            ScheduleIconPass();
        };

        // 恢复上次的卡片视图（不触发布局重算：此时还没 InitializeComponent）
        _vm.SetView(string.Equals(_settings.CardView, "compact", StringComparison.OrdinalIgnoreCase));

        _processMonitor.SnapshotUpdated += OnProcessSnapshot;

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        ContentRendered += async (_, _) => await ReloadAsync();
        Closed += (_, _) =>
        {
            DisposeTray();
            _processMonitor.Dispose();
            _hotkeys?.Dispose();
            _sta.Dispose();
        };

        StartDebugServerIfEnabled();
        SetupTray();
    }

    private MainViewModel ViewModel => _vm;

    /// <summary>被外部（第二实例 / 托盘 / 热键）唤出。</summary>
    public void ShowAndFocus()
    {
        _suppressDeactivateHide = true;
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        WindowActivation.ForceForeground(_hwnd);
        Focus();
        EnsureCursorVisible();
        _suppressDeactivateHide = false;
        _processMonitor.SetActive(true);
        ScheduleIconPass();
    }

    // ---------------------------------------------------------------- 窗口层

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        _isDarkTheme = IsSystemDarkTheme();

        Themes.ThemeManager.Changed += OnThemeChanged;
        TryApplyMica();
        TryLoadIcon();
        RegisterHotkey();

        Activated += (_, _) => _hasBeenActivated = true;
    }

    /// <summary>失焦等同于按 Esc：收起到托盘。</summary>
    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        if (!_hasBeenActivated || _suppressDeactivateHide || !IsVisible)
        {
            return;
        }

        HidePanel();
    }

    private void TryApplyMica(int attempt = 0)
    {
        if (SafeMode.Has("mica") || _hwnd == IntPtr.Zero || _micaApplied)
        {
            return;
        }

        bool applied;
        try
        {
            applied = WindowBackdrop.TryApply(_hwnd, BackdropKind.Mica, _isDarkTheme);
        }
        catch
        {
            applied = false;
        }

        Diagnostics.Write($"mica: attempt={attempt} supported={WindowBackdrop.IsSupported} applied={applied} dark={_isDarkTheme}");

        if (applied)
        {
            _micaApplied = true;
            RootBorder.Background = null;
            return;
        }

        if (attempt == 0)
        {
            Dispatcher.BeginInvoke(new Action(() => TryApplyMica(1)), DispatcherPriority.ApplicationIdle);
        }
        else if (attempt == 1)
        {
            Activated += OnFirstActivatedRetryMica;
        }
        else
        {
            RootBorder.Background = new SolidColorBrush(_isDarkTheme
                ? Color.FromRgb(0x20, 0x20, 0x20)
                : Color.FromRgb(0xF3, 0xF3, 0xF3));
        }
    }

    private void OnFirstActivatedRetryMica(object? sender, EventArgs e)
    {
        Activated -= OnFirstActivatedRetryMica;
        Dispatcher.BeginInvoke(new Action(() => TryApplyMica(2)), DispatcherPriority.Background);
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

            IconBitmapDecoder decoder;
            using (var stream = resource.Stream)
            {
                decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            }

            if (decoder.Frames.Count == 0)
            {
                return;
            }

            Icon = decoder.Frames.OrderByDescending(f => f.PixelWidth).First();
            TitleIcon.Source = decoder.Frames.OrderBy(f => Math.Abs(f.PixelWidth - 48)).First();
        }
        catch
        {
            // 图标资源缺失时留空
        }
    }

    private void RegisterHotkey()
    {
        try
        {
            _hotkeys = new HotkeyManager(_hwnd);
            _hotkeys.Pressed += (_, _) => Dispatcher.BeginInvoke(new Action(ShowAndFocus), DispatcherPriority.Input);

            var attempts = new List<(ModifierKeys Modifiers, Key Key)>();
            if (HotkeyManager.Parse(_settings.Hotkey) is { } configured)
            {
                attempts.Add(configured);
            }

            attempts.Add((ModifierKeys.Control | ModifierKeys.Alt, Key.Space));
            attempts.Add((ModifierKeys.Control | ModifierKeys.Shift, Key.Space));
            attempts.Add((ModifierKeys.Control | ModifierKeys.Alt, Key.Q));

            foreach (var (modifiers, key) in attempts)
            {
                if (_hotkeys.Register(HotkeyId, modifiers, key))
                {
                    _effectiveHotkey = HotkeyManager.Format(modifiers, key);
                    break;
                }
            }
        }
        catch
        {
            _effectiveHotkey = "（未注册）";
        }

        UpdateHelpText();
    }

    private void UpdateHelpText()
    {
        HelpTip.Text =
            "面板列出每个沙盒内桌面与开始菜单里的应用快捷方式，点击即在对应沙盒中启动。" +
            Environment.NewLine + "每个沙盒底部的「标准应用程序」分组内置 cmd、资源管理器等系统工具，默认折叠。" +
            Environment.NewLine + "卡片右上角绿点 / 绿框表示该程序正在这个沙盒里运行（宿主机上跑的同名程序不算）。" +
            Environment.NewLine + Environment.NewLine +
            "热键 " + _effectiveHotkey + " 随时唤出；唤出后直接输入筛选，方向键选择，Enter 启动。" +
            Environment.NewLine + "Ctrl+Shift+V 切换紧凑视图（图标 + 标题，路径隐藏）。" +
            Environment.NewLine + "Esc 或窗口失焦都会收起到托盘；托盘图标右键可退出。";
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        Focus();
        _processMonitor.Start();
        _processMonitor.RefreshNow();
        ScheduleIconPass();
    }

    private void OnProcessSnapshot(object? sender, RunningSnapshot snapshot) =>
        Dispatcher.BeginInvoke(new Action(() => _vm.ApplyRunningSnapshot(snapshot)), DispatcherPriority.Background);

    private void OnThemeChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _isDarkTheme = IsSystemDarkTheme();
            if (_hwnd != IntPtr.Zero)
            {
                try
                {
                    WindowBackdrop.SetDarkMode(_hwnd, _isDarkTheme);
                }
                catch
                {
                    // 深浅色切换失败不影响内容
                }
            }

            if (!_micaApplied)
            {
                RootBorder.Background = new SolidColorBrush(_isDarkTheme
                    ? Color.FromRgb(0x20, 0x20, 0x20)
                    : Color.FromRgb(0xF3, 0xF3, 0xF3));
            }
        }), DispatcherPriority.Loaded);

    // ---------------------------------------------------------------- 加载

    private async Task ReloadAsync()
    {
        _scanCts?.Cancel();
        _scanCts?.Dispose();
        var cts = new CancellationTokenSource();
        _scanCts = cts;

        ResetIconRequests();
        _vm.Icons.ResetStatistics();

        await _vm.LoadAsync(cts.Token);

        if (!cts.Token.IsCancellationRequested)
        {
            // 预热布局：把首帧生成成本留在加载阶段（此时进度条仍在显示）
            _vm.SetBusy(true);
            _vm.WarmUp();
            UpdateLayout();
            _vm.ApplyFilter();
            _vm.SetBusy(false);

            // 界面已经出屏（字母占位），现在才按视口开始补图标
            ScheduleIconPass();
        }
    }

    // ---------------------------------------------------------------- 输入

    private void OnWindowTextInput(object sender, TextCompositionEventArgs e)
    {
        if (e.Text.Length == 0 || char.IsControl(e.Text[0]) || FilterBox.IsKeyboardFocusWithin)
        {
            return;
        }

        FilterBox.Focus();
        FilterBox.Text += e.Text;
        FilterBox.CaretIndex = FilterBox.Text.Length;
        e.Handled = true;
    }

    private void OnFilterTextChanged(object sender, TextChangedEventArgs e)
    {
        FilterPlaceholder.Visibility = FilterBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        MarkBackdropDirty();
        ScheduleIconPass();
    }

    private void OnFilterPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Down or Key.Up)
        {
            _vm.MoveSelection(0, e.Key == Key.Down ? 1 : -1);
            e.Handled = true;
        }
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

        switch (e.Key)
        {
            case Key.Escape:
                // 层级：文件夹弹窗 → 筛选词 → 收起面板
                if (_vm.IsFolderOpen)
                {
                    _vm.CloseFolder();
                    e.Handled = true;
                    break;
                }

                if (FilterBox.Text.Length > 0)
                {
                    FilterBox.Text = string.Empty;
                    FilterBox.Focus();
                }
                else
                {
                    HidePanel();
                }

                e.Handled = true;
                break;

            case Key.Enter:
                _vm.ActivateSelected();
                e.Handled = true;
                break;

            // 光标停在「标准应用程序」标题行上时，Space 用来展开/收起（否则空格照常输入到筛选框）
            case Key.Space when _vm.IsStandardHeaderSelected || _vm.IsFolderSelected || _vm.IsFolderOpen:
                _vm.ActivateSelected();
                e.Handled = true;
                break;

            case Key.Left:
                _vm.MoveSelection(-1, 0);
                e.Handled = true;
                break;

            case Key.Right:
                _vm.MoveSelection(1, 0);
                e.Handled = true;
                break;

            case Key.Up:
                _vm.MoveSelection(0, -1);
                e.Handled = true;
                break;

            case Key.Down:
                _vm.MoveSelection(0, 1);
                e.Handled = true;
                break;

            case Key.Home:
                _vm.MoveToEdge(false);
                e.Handled = true;
                break;

            case Key.End:
                _vm.MoveToEdge(true);
                e.Handled = true;
                break;

            case Key.PageUp:
                _vm.MovePage(-PageRows());
                e.Handled = true;
                break;

            case Key.PageDown:
                _vm.MovePage(PageRows());
                e.Handled = true;
                break;

            case Key.V when (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) ==
                            (ModifierKeys.Control | ModifierKeys.Shift):
                ToggleCardView();
                e.Handled = true;
                break;

            case Key.F5:
                _ = ReloadAsync();
                e.Handled = true;
                break;

            case Key.Back when !FilterBox.IsKeyboardFocusWithin:
                if (FilterBox.Text.Length > 0)
                {
                    FilterBox.Text = FilterBox.Text[..^1];
                }

                e.Handled = true;
                break;
        }

        if (ctrl && e.Key is >= Key.D1 and <= Key.D9)
        {
            var index = e.Key - Key.D1;
            var rails = _vm.RailItems;
            if (index < rails.Count)
            {
                _vm.SelectRail(rails[index]);
            }

            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.F)
        {
            FilterBox.Focus();
            FilterBox.SelectAll();
            e.Handled = true;
        }
    }

    /// <summary>一屏能放几行卡片（行高由布局实测，DPI / 视图模式都不用二次换算）。</summary>
    private int PageRows()
    {
        var height = CardsScroller.ViewportHeight;
        return Math.Max(1, (int)Math.Floor(height / _rowPitch));
    }

    /// <summary>
    /// 从真实布局里量出「每行几张卡、列步进、行步进」。
    /// 不用宽度除以常量去猜：那条路会漏掉 ScrollViewer 的 Padding、竖向滚动条占位、
    /// 布局取整（1.5 倍 DPI 下 918 / 184 = 4.99 却算出 5 列）和视图模式差异。
    /// 这里直接看 WrapPanel 里第一个子元素所在行有几个同级元素，量出来的就是用户看到的。
    /// </summary>
    private void MeasureCardGrid()
    {
        if (!IsVisible || GroupsHost.ActualWidth <= 0)
        {
            return;
        }

        var panel = FindFirstCardsPanel(NavRoot);
        if (panel is null)
        {
            return;
        }

        var positions = new List<Point>(panel.Children.Count);
        foreach (var child in panel.Children)
        {
            if (child is not FrameworkElement element || element.DataContext is not AppCardViewModel)
            {
                continue;
            }

            try
            {
                positions.Add(element.TransformToAncestor(panel).Transform(new Point(0, 0)));
            }
            catch (InvalidOperationException)
            {
                // 元素不在可视树里时跳过
            }
        }

        if (positions.Count == 0)
        {
            return;
        }

        var first = positions[0];
        var firstRow = positions.Where(p => Math.Abs(p.Y - first.Y) < 0.5).ToList();
        var observedInRow = firstRow.Count;

        // 列步进优先取「卡片槽位宽度」（ContentPresenter 的宽度＝卡片宽＋左右外边距），
        // 它在换视图后立刻就是新值，比等 WrapPanel 重新换行更早可用。
        var slotWidth = panel.Children[0] is FrameworkElement slot ? slot.ActualWidth : 0;
        var slotHeight = panel.Children[0] is FrameworkElement slotRow ? slotRow.ActualHeight : 0;

        // 其次用同一行相邻两张卡的真实间距
        var step = observedInRow >= 2 ? firstRow[1].X - firstRow[0].X : 0;
        if (slotWidth > 0.5)
        {
            step = slotWidth;
        }

        if (step > 0.5 && Math.Abs(step - _cardStep) > 0.5)
        {
            _cardStep = step;
            // 步进变了（换视图 / 首次布局）：等这一轮布局落定后再量一次，行模型才不会停在旧列数
            ScheduleIconPass();
        }

        // 每行能放几张：WrapPanel 的真实可用宽度 ÷ 实测槽位宽度。
        // 不能只看「第一行有几张」——某个沙盒只有 3 张卡时那只是 3，不是容量。
        int columns;
        if (_cardStep > 0.5)
        {
            var capacity = Math.Max(1, (int)Math.Floor((panel.ActualWidth + 0.5) / _cardStep));

            // 布局还没落定（例如刚换视图，所有卡片还叠在原位）时同一"行"会数出比容量更多的卡：
            // 这一拍不信，下一拍再量。
            if (observedInRow > capacity)
            {
                ScheduleIconPass();
                return;
            }

            columns = capacity;
        }
        else
        {
            columns = Math.Max(1, observedInRow);
        }

        // 行步进优先取槽位高度（换视图后立刻可用）；拿不到时再看下一行的实际 Y 差
        if (slotHeight > 0.5)
        {
            _rowPitch = slotHeight;
        }
        else
        {
            var nextRow = positions
                .Where(p => p.Y - first.Y > 1)
                .OrderBy(p => p.Y)
                .FirstOrDefault();
            var rowPitch = nextRow.Y - first.Y;
            if (positions.Any(p => p.Y - first.Y > 1) && rowPitch > 0.5)
            {
                _rowPitch = rowPitch;
            }
        }

        if (columns > 0 && columns != _vm.Columns)
        {
            _vm.SetColumns(columns);
        }

        _gridPassColumns = columns;
        _gridPassStep = _cardStep;
        _gridPassRowPitch = _rowPitch;
        _gridPassObserved = observedInRow;
        _gridPassPanelWidth = panel.ActualWidth;
        _gridPassCount++;
    }

    /// <summary>分组块之间的水平间距（和 SectionTemplate 的右外边距一致）。</summary>
    private const double SectionGap = 12;

    /// <summary>当前导航/测量所属的根：平时是卡片区，文件夹弹窗打开时是弹窗里的列表。</summary>
    private FrameworkElement NavRoot => _vm.IsFolderOpen ? FolderItemsHost : GroupsHost;

    /// <summary>最近一次视觉导航采集到的原始坐标（调试用）。</summary>
    private List<(string Name, double X, double Y)> _navRaw = new();

    private long _navPasses;
    private int _navFound;
    private string _navRoot = "-";
    private int _navButtons;
    private int _navVisibleButtons;

    /// <summary>
    /// 给每个沙盒分组块定宽：标准视图＝整行宽（等价于纵向堆叠）；
    /// 紧凑视图＝按卡片数算出的块宽，窗口够宽时下一个沙盒的块就能并排到同一行。
    /// 返回是否真的变了（变了才需要重新走一遍布局）。
    /// </summary>
    private bool ArrangeSectionBlocks()
    {
        var available = GroupsHost.ActualWidth;
        if (!IsVisible || _vm.IsFolderOpen || available <= 0 || _cardStep <= 0.5)
        {
            return false;
        }

        var compact = _vm.Metrics.IsCompact;
        var changed = false;

        if (!compact)
        {
            var fullWidth = Math.Max(_cardStep, available - SectionGap);
            foreach (var section in _vm.Sections)
            {
                if (section.IsVisible)
                {
                    changed |= SetBlockWidth(section, fullWidth);
                }
            }

            return changed;
        }

        var maxColumns = Math.Max(1, (int)Math.Floor((available + 0.5) / _cardStep));
        var used = 0.0;

        foreach (var section in _vm.Sections)
        {
            if (!section.IsVisible)
            {
                continue;
            }

            // 同一行要放下的块内元素：普通应用 + 末尾的文件夹块（紧凑视图里标准应用在弹窗里，不占位）
            var cards = Math.Max(1, section.VisibleAppCount + 1);

            // 块宽还要容得下沙盒标题（否则标题会被裁掉），按实测文字宽度算最少几列
            var perRow = Math.Min(maxColumns, Math.Max(cards, MeasureTitleColumns(section, _cardStep)));
            var width = perRow * _cardStep;

            if (used > 0 && used + width + SectionGap > available)
            {
                used = 0;
            }

            changed |= SetBlockWidth(section, width);
            used += width + SectionGap;
        }

        return changed;
    }

    /// <summary>标题（沙盒名 + 分组标签）按 12px SemiBold 量出来需要几列卡片宽度。</summary>
    private int MeasureTitleColumns(SandboxSectionViewModel section, double step)
    {
        if (step <= 0.5)
        {
            return 1;
        }

        var text = section.HasGroupLabel
            ? section.Name + "  · " + section.GroupLabel
            : section.Name;

        try
        {
            var typeface = new Typeface(
                (FontFamily)FindResource("AppFontFamily"),
                FontStyles.Normal,
                FontWeights.SemiBold,
                FontStretches.Normal);

            var formatted = new FormattedText(
                text,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                typeface,
                12,
                Brushes.Transparent,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            return Math.Max(1, (int)Math.Ceiling((formatted.WidthIncludingTrailingWhitespace + 16) / step));
        }
        catch
        {
            return 1;
        }
    }

    private static bool SetBlockWidth(SandboxSectionViewModel section, double width)
    {
        if (Math.Abs(section.BlockWidth - width) < 0.5)
        {
            return false;
        }

        section.BlockWidth = width;
        return true;
    }

    /// <summary>
    /// 按真实布局重建导航行：把每张卡、每个「标准应用程序」标题行的中心点捞出来，
    /// 纵向重叠的归为同一视觉行，行内按 X 排序后交给 ViewModel。
    /// 这样紧凑视图里两个沙盒块并排时，上下移动也能落在「正下方那张卡」上。
    /// </summary>
    private void RebuildVisualNav()
    {
        if (!IsVisible || _rowPitch <= 0.5)
        {
            return;
        }

        var root = NavRoot;
        var found = new List<(INavItem Item, double CenterX, double CenterY)>(128);
        CollectNavItems(root, found);

        _navPasses++;
        _navFound = found.Count;
        _navRoot = ReferenceEquals(root, FolderItemsHost) ? "folder" : "groups";
        (_navButtons, _navVisibleButtons) = CountButtons(root);

        if (found.Count == 0)
        {
            // 重排中间态可能一张都收集不到（例如弹窗刚显示、容器还没 realise）：
            // 这时保留上一版行模型，别把光标和导航一起清掉。
            // 真的「筛到空」时 ApplyFilter → BuildRows 已经把行模型清空了，这里不清也不会残留。
            return;
        }

        _navRaw = found.Select(f => (f.Item.NavTitle, Math.Round(f.CenterX, 1), Math.Round(f.CenterY, 1))).ToList();

        found.Sort((a, b) => a.CenterY.CompareTo(b.CenterY));

        var rows = new List<VisualRow>(found.Count);
        var taken = new bool[found.Count];
        var tolerance = _rowPitch * 0.45;

        for (var i = 0; i < found.Count; i++)
        {
            if (taken[i])
            {
                continue;
            }

            var anchorY = found[i].CenterY;
            var rowItems = new List<(INavItem Item, double CenterX)>();

            for (var j = i; j < found.Count; j++)
            {
                if (taken[j] || Math.Abs(found[j].CenterY - anchorY) > tolerance)
                {
                    continue;
                }

                taken[j] = true;
                rowItems.Add((found[j].Item, found[j].CenterX));
            }

            rowItems.Sort((a, b) => a.CenterX.CompareTo(b.CenterX));
            rows.Add(new VisualRow(
                rowItems.Select(r => r.Item).ToList(),
                rowItems.Select(r => r.CenterX).ToList()));
        }

        _vm.ApplyVisualLayout(rows);
    }

    private static (int Total, int Visible) CountButtons(DependencyObject root)
    {
        var total = 0;
        var visible = 0;

        void Walk(DependencyObject node)
        {
            var count = VisualTreeHelper.GetChildrenCount(node);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(node, i);
                if (child is Button button)
                {
                    total++;
                    if (button.IsVisible)
                    {
                        visible++;
                    }
                }

                Walk(child);
            }
        }

        Walk(root);
        return (total, visible);
    }

    private void CollectNavItems(
        DependencyObject root,
        List<(INavItem Item, double CenterX, double CenterY)> into)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (child is Button button && button.IsVisible)
            {
                // 卡片的 DataContext 就是导航项；分组标题行的 Button 绑的是沙盒分组，导航项在 Tag 上
                var item = button.DataContext as INavItem ?? button.Tag as INavItem;
                if (item is not null)
                {
                    try
                    {
                        // 统一相对窗口取坐标：卡片区与文件夹弹窗是两棵不同的子树，只要求「同一参照系」
                        var topLeft = button.TransformToAncestor(this).Transform(new Point(0, 0));
                        into.Add((item, topLeft.X + button.ActualWidth / 2, topLeft.Y + button.ActualHeight / 2));
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
                    {
                        // 元素不在可视树里，跳过
                    }
                }
            }

            CollectNavItems(child, into);
        }
    }

    /// <summary>找到第一个有卡片子元素的 WrapPanel（跳过空的沙盒分组）。</summary>
    private static WrapPanel? FindFirstCardsPanel(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            // 只认真正可见的面板：折叠着（或整段隐藏）的面板子项还停在旧坐标，量出来会多算列
            if (child is WrapPanel { IsVisible: true } panel && panel.Children.Count > 0 &&
                panel.Children[0] is FrameworkElement { DataContext: AppCardViewModel })
            {
                return panel;
            }

            var found = FindFirstCardsPanel(child);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    // ---------------------------------------------------------------- 鼠标

    private void OnRailItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RailItemViewModel item })
        {
            _vm.SelectRail(item);
            MarkBackdropDirty();
            ScheduleIconPass();
        }
    }

    private void OnCardClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AppCardViewModel card })
        {
            _vm.Launch(card);
        }
    }

    private void OnStandardToggleClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SandboxSectionViewModel section })
        {
            _vm.ToggleStandard(section);
            ScheduleIconPass();
        }
    }

    private void OnCardsViewportChanged(object sender, SizeChangedEventArgs e) =>
        // 列数不再靠「宽度 ÷ 常量」猜，由 MeasureCardGrid() 在节流 tick 里按真实布局量
        ScheduleIconPass();

    private void OnCardsScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // 滚动会改变弹窗里该看到的背景，快照要重截（下次打开时）
        if (e.VerticalChange != 0 || e.HorizontalChange != 0)
        {
            MarkBackdropDirty();
        }

        ScheduleIconPass();
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) => _ = ReloadAsync();

    private void OnCloseClick(object sender, RoutedEventArgs e) => HidePanel();

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed &&
            e.OriginalSource is not Button &&
            e.OriginalSource is not TextBox)
        {
            try
            {
                DragMove();
            }
            catch
            {
                // DragMove 在特定状态下会抛异常，忽略
            }
        }
    }

    // ---------------------------------------------------------------- 滚动 / 提示

    private void EnsureCursorVisible()
    {
        if (_vm.SelectedItem is { } item)
        {
            ScrollItemIntoView(item);
        }
    }

    /// <summary>Ctrl+Shift+V：切换标准 / 紧凑视图。</summary>
    private void ToggleCardView()
    {
        _vm.ToggleView();
        ApplyCardViewLayout();
    }

    /// <summary>
    /// 视图切换后的布局收尾：指标（CardMetrics）已经变了，这里让绑定把新尺寸写到控件上、
    /// 立即重算布局，再标脏视口（列数测量 + 图标补图在节流 tick 里按新步进重跑），
    /// 最后把选中卡滚回可视区。不重建集合、不换模板，卡片靠绑定自动重排。
    /// </summary>
    private void ApplyCardViewLayout()
    {
        UpdateLayout();
        RunViewportPassNow();

        if (_vm.SelectedItem is { } item)
        {
            ScrollItemIntoView(item);
        }
        else
        {
            EnsureCursorVisible();
        }
    }

    private void ScrollItemIntoView(INavItem item) =>
        Dispatcher.BeginInvoke(new Action(() =>
        {
            // 文件夹弹窗固定居中、不滚动，不需要把项滚进视野
            if (_vm.IsFolderOpen)
            {
                return;
            }

            var container = FindItemContainer(GroupsHost, item);
            container?.BringIntoView();
        }), DispatcherPriority.Loaded);

    /// <summary>文件夹弹窗开/关后：刷新毛玻璃背景、重排布局、重建行模型（现在量的是弹窗里的列表）并补图标。</summary>
    private void RefreshFolderLayer()
    {
        var stopwatch = Stopwatch.StartNew();
        UpdateBackdrop();
        var backdrop = stopwatch.Elapsed.TotalMilliseconds;
        stopwatch.Restart();

        UpdateLayout();
        var layout = stopwatch.Elapsed.TotalMilliseconds;
        stopwatch.Restart();

        RunViewportPassNow();
        LastFolderTiming = (backdrop, layout, stopwatch.Elapsed.TotalMilliseconds);
    }

    /// <summary>最近一次开/关弹窗的分段耗时（毫秒），供性能诊断。</summary>
    public static (double Backdrop, double Layout, double Viewport) LastFolderTiming { get; private set; }

    private void OnFolderClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: StandardFolderItem folder })
        {
            _vm.OpenFolder(folder);
        }
    }

    private void OnFolderScrimClick(object sender, MouseButtonEventArgs e)
    {
        _vm.CloseFolder();
        e.Handled = true;
    }

    /// <summary>
    /// 找某个导航项对应的容器：卡片的 DataContext 就是它自己；
    /// 「标准应用程序」标题行的 Button 绑的是沙盒分组，所以额外比对 Tag（XAML 里 Tag="{Binding Header}"）。
    /// </summary>
    private static FrameworkElement? FindItemContainer(DependencyObject root, INavItem item)
    {
        if (root is Button button &&
            (ReferenceEquals(button.DataContext, item) || ReferenceEquals(button.Tag, item)))
        {
            return button;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var found = FindItemContainer(VisualTreeHelper.GetChild(root, i), item);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    // ---------------------------------------------------------------- 图标懒加载

    /// <summary>
    /// 立刻跑一次视口重算（列数测量 + 图标补图），不等 70ms 的节流计时器：
    /// 视图切换后紧接着的 BringIntoView 与调试返回都要用新步进量出来的行模型。
    /// </summary>
    private void RunViewportPassNow()
    {
        ScheduleIconPass();
        OnIconThrottleTick(_iconThrottle, EventArgs.Empty);
    }

    /// <summary>把「视口需要重算」标脏，由节流计时器合并成一次重算。</summary>
    private void ScheduleIconPass()
    {
        _iconPassDirty = true;
        if (!_iconThrottle.IsEnabled)
        {
            _iconThrottle.Start();
        }
    }

    private void OnIconThrottleTick(object? sender, EventArgs e)
    {
        if (!_iconPassDirty)
        {
            _iconThrottle.Stop();
            return;
        }

        _iconPassDirty = false;

        // 布局可能刚变过（窗口缩放 / 换视图 / 切分组）：
        // 1) 量卡片步进与列数；2) 给沙盒分组块定宽（紧凑视图可能并排）；
        // 3) 按真实布局重建导航行；4) 补图标
        MeasureCardGrid();

        if (ArrangeSectionBlocks())
        {
            UpdateLayout();
            MeasureCardGrid();
        }

        RebuildVisualNav();
        RunIconPass();
    }

    /// <summary>
    /// 视口重算：只对落在 <c>CardsScroller</c> 可视范围内（上下各预取一行）且还没有图标的卡片请求图标；
    /// 视口内的走 Viewport 优先级，边界预取带走 Prefetch 优先级，其余卡片一律不请求。
    /// 已经拿到图标（含抽取失败被缓存）的卡片不再重复请求。
    /// </summary>
    private void RunIconPass()
    {
        if (SafeMode.Has("icons") || _vm.IsBusy || !IsVisible || CardsScroller.ViewportHeight <= 0)
        {
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var viewportHeight = CardsScroller.ViewportHeight;
        var prefetch = _rowPitch * IconPrefetchRows;

        var containers = new List<(Button Button, AppCardViewModel Card)>(128);
        CollectCardContainers(NavRoot, containers);

        var inViewport = 0;
        var prefetched = 0;
        foreach (var (button, card) in containers)
        {
            if (card.HasIcon || _iconRequested.Contains(card) || !button.IsVisible)
            {
                continue;
            }

            if (_vm.IsFolderOpen)
            {
                // 文件夹弹窗里没有滚动：全部请求，但仍是按需（打开时才发生）
                inViewport++;
                RequestIcon(card, IconPriority.Viewport);
                continue;
            }

            double top;
            try
            {
                top = button.TransformToAncestor(CardsScroller).Transform(new Point(0, 0)).Y;
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            var bottom = top + button.ActualHeight;
            if (bottom <= -prefetch || top >= viewportHeight + prefetch)
            {
                continue;
            }

            var insideViewport = bottom > 0 && top < viewportHeight;
            if (insideViewport)
            {
                inViewport++;
            }
            else
            {
                prefetched++;
            }

            RequestIcon(card, insideViewport ? IconPriority.Viewport : IconPriority.Prefetch);
        }

        // 紧凑视图里文件夹块要显示 3×3 预览，这些卡片不在视口收集里，单独按需请求
        if (_vm.Metrics.IsCompact && !_vm.IsFolderOpen)
        {
            foreach (var section in _vm.Sections)
            {
                if (!section.IsVisible || !section.Folder.IsVisible)
                {
                    continue;
                }

                foreach (var card in section.Folder.PreviewApps)
                {
                    if (!card.HasIcon && !_iconRequested.Contains(card))
                    {
                        RequestIcon(card, IconPriority.Prefetch);
                    }
                }
            }
        }

        _iconPassViewport = inViewport;
        _iconPassPrefetch = prefetched;
        _iconPassMs = stopwatch.Elapsed.TotalMilliseconds;
        _iconPassCount++;
    }

    private void RequestIcon(AppCardViewModel card, IconPriority priority)
    {
        var path = ResolveIconPath(card);
        if (path.Length == 0 || !_iconRequested.Add(card))
        {
            return;
        }

        _vm.Icons.Request(
            path,
            icon =>
            {
                if (icon is not null)
                {
                    card.Icon = icon;
                }
            },
            priority: priority);
    }

    /// <summary>
    /// 取图标用的真实文件路径：优先 Core 解析出的沙盒内路径（<see cref="DiscoveredApp.IconPath"/>，宿主机上确实存在），
    /// 再兜底一次沙盒映射。同一路径只解析一次。
    /// </summary>
    private string ResolveIconPath(AppCardViewModel card)
    {
        var candidate = card.App.IconPath;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return string.Empty;
        }

        if (_iconPathCache.TryGetValue(candidate, out var cached))
        {
            return cached;
        }

        var resolved = candidate;
        if (!File.Exists(candidate))
        {
            var section = _vm.Sections.FirstOrDefault(
                s => string.Equals(s.Name, card.App.SandboxName, StringComparison.OrdinalIgnoreCase));
            if (section is not null)
            {
                resolved = SandboxPathMapper.MapToSandbox(section.Box, candidate) ?? candidate;
            }
        }

        _iconPathCache[candidate] = resolved;
        return resolved;
    }

    private void ResetIconRequests()
    {
        _iconRequested.Clear();
        _iconPathCache.Clear();
    }

    /// <summary>一次遍历收集所有卡片按钮（WrapPanel 不虚拟化，容器稳定，避免逐卡 FindCardContainer 的 O(n²)）。</summary>
    private static void CollectCardContainers(DependencyObject root, List<(Button Button, AppCardViewModel Card)> into)
    {
        if (root is Button { DataContext: AppCardViewModel card } button)
        {
            into.Add((button, card));
            return;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            CollectCardContainers(VisualTreeHelper.GetChild(root, i), into);
        }
    }

    private void ShowTransientHint(string text)
    {
        _hintTimer?.Stop();
        FilterPlaceholder.Text = text;
        FilterPlaceholder.Foreground = (Brush)FindResource("TextSecondaryBrush");
        FilterPlaceholder.Visibility = Visibility.Visible;

        _hintTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _hintTimer.Tick += (_, _) =>
        {
            _hintTimer?.Stop();
            FilterPlaceholder.Text = "搜索应用";
            FilterPlaceholder.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");
            FilterPlaceholder.Visibility = FilterBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        };
        _hintTimer.Start();
    }

    private static bool IsSystemDarkTheme()
    {
        try
        {
            return Themes.ThemeManager.IsInitialized ? Themes.ThemeManager.IsDark : ReadSystemDark();
        }
        catch
        {
            return ReadSystemDark();
        }
    }

    private static bool ReadSystemDark()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int i && i == 0;
        }
        catch
        {
            return true;
        }
    }
}
