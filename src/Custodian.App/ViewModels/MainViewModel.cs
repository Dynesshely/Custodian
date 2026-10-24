using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using Custodian.App.Services;
using Custodian.Core.Discovery;
using Custodian.Core.Launching;
using Custodian.Core.Sandboxie;
using Custodian.Core.Settings;

namespace Custodian.App.ViewModels;

/// <summary>
/// 面板状态与交互的唯一真相：
/// - 沙盒/卡片的发现与缓存（只扫一次，筛选只切换可见性）
/// - 左栏选择、搜索过滤、网格光标导航、启动
/// - 「运行中」状态刷新
/// View 只负责渲染、输入转发与窗口层能力（Mica/托盘/热键）。
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly CustodianSettings _settings;
    private readonly IShortcutReader _reader;
    private readonly IconService _icons;
    private readonly StaWorker _sta;
    private readonly List<SandboxSectionViewModel> _sections = new();
    private readonly List<NavRow> _rows = new();

    /// <summary>与 <see cref="_rows"/> 对应：每行内各项的水平中心（DIP），竖直移动时挑最近列用。</summary>
    private readonly List<List<double>> _centers = new();
    private readonly Dictionary<INavItem, (int Row, int Column)> _positions = new();

    /// <summary>我们自己启动的沙盒进程（系统自带工具无法从镜像路径归属，只能靠启动记录）。</summary>
    private readonly Dictionary<(string Box, string Process), HashSet<int>> _launchedPids = new();

    /// <summary>刚发起启动、还在等目标进程出现的基线快照。</summary>
    private readonly Dictionary<(string Box, string Process), (DateTime At, HashSet<int> Baseline)> _pendingLaunch = new();

    /// <summary>后台采样要用的输入（沙盒列表 + 关心的进程名），只在 UI 线程重建。</summary>
    private CapturePlan _capturePlan = new(Array.Empty<SandboxBox>(), Array.Empty<string>());

    /// <summary>启动后多久内认领新出现的进程（超过就当启动失败/被拦截）。</summary>
    private static readonly TimeSpan LaunchClaimWindow = TimeSpan.FromSeconds(10);

    /// <summary>因为搜索命中而自动展开的分组，清空搜索后要收回去（用户手动展开的不动）。</summary>
    private readonly HashSet<SandboxSectionViewModel> _autoExpanded = new();

    private RailItemViewModel? _selectedRail;
    private INavItem? _selectedItem;
    private string _filterText = string.Empty;
    private bool _isBusy;
    private int _columns = 4;

    public MainViewModel(
        CustodianSettings settings,
        IShortcutReader reader,
        IconService icons,
        StaWorker sta)
    {
        _settings = settings;
        _reader = reader;
        _icons = icons;
        _sta = sta;
    }

    /// <summary>请求隐藏面板（启动应用后 / Esc / 失焦）。</summary>
    public event EventHandler? HideRequested;

    /// <summary>需要在工具栏区域给一条临时提示。</summary>
    public event EventHandler<string>? HintRequested;

    /// <summary>光标换了卡片，View 负责滚到可见位置。</summary>
    public event EventHandler<INavItem>? SelectionMoved;

    /// <summary>筛选 / 左栏 / 分组展开变化后，View 需要重新量布局并重建行模型。</summary>
    public event EventHandler? LayoutInvalidated;

    private void InvalidateLayout() => LayoutInvalidated?.Invoke(this, EventArgs.Empty);

    /// <summary>紧凑视图里打开了「标准应用程序」文件夹弹窗 / 关闭了它。</summary>
    public event EventHandler? FolderOpened;

    public event EventHandler? FolderClosed;

    private StandardFolderItem? _activeFolder;

    /// <summary>是否正在显示文件夹弹窗（紧凑视图）。</summary>
    public bool IsFolderOpen => _activeFolder is not null;

    /// <summary>当前打开的文件夹块。</summary>
    public StandardFolderItem? ActiveFolder => _activeFolder;

    /// <summary>弹窗里要列出的标准应用。</summary>
    public IReadOnlyList<AppCardViewModel> ActiveFolderApps =>
        _activeFolder?.Section.StandardApps ?? (IReadOnlyList<AppCardViewModel>)Array.Empty<AppCardViewModel>();

    public string ActiveFolderTitle => _activeFolder is null
        ? string.Empty
        : "标准应用程序 · " + _activeFolder.Section.SandboxDisplay;

    public string ActiveFolderCountText => _activeFolder?.Section.StandardApps.Count.ToString() ?? "0";

    /// <summary>打开某个沙盒的「标准应用程序」文件夹弹窗。</summary>
    public void OpenFolder(StandardFolderItem folder)
    {
        if (folder.Section.StandardApps.Count == 0)
        {
            return;
        }

        _activeFolder = folder;
        RaiseFolderState();

        // 弹窗里列的是「这一组的全部标准应用」，不受内联筛选影响（防止它们在标准视图里被置为不可见）
        foreach (var app in folder.Section.StandardApps)
        {
            app.IsVisible = true;
        }

        // 光标先落到弹窗里的第一项（布局量出来之前也能用键盘）
        var first = folder.Section.StandardApps.FirstOrDefault(c => c.IsVisible)
                    ?? folder.Section.StandardApps.FirstOrDefault();
        if (first is not null)
        {
            Select(first, scroll: false);
        }

        FolderOpened?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>关闭文件夹弹窗；关闭后光标回到那个文件夹块上。</summary>
    public void CloseFolder(bool restoreSelection = true)
    {
        var folder = _activeFolder;
        if (folder is null)
        {
            return;
        }

        _activeFolder = null;
        RaiseFolderState();
        FolderClosed?.Invoke(this, EventArgs.Empty);

        if (restoreSelection)
        {
            Select(folder, scroll: true);
        }
    }

    private void RaiseFolderState()
    {
        Raise(nameof(IsFolderOpen));
        Raise(nameof(ActiveFolder));
        Raise(nameof(ActiveFolderApps));
        Raise(nameof(ActiveFolderTitle));
        Raise(nameof(ActiveFolderCountText));
    }

    public ObservableCollection<RailItemViewModel> RailItems { get; } = new();

    public ObservableCollection<SandboxSectionViewModel> Sections { get; } = new();

    public CustodianSettings Settings => _settings;

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (!Set(ref _filterText, value ?? string.Empty))
            {
                return;
            }

            ApplyFilter();
            SelectFirstVisible();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => Set(ref _isBusy, value);
    }

    /// <summary>供 View 在「预热布局」阶段维持进度条。</summary>
    public void SetBusy(bool busy) => IsBusy = busy;

    public bool HasVisibleCards => _rows.Count > 0;

    /// <summary>当前选中的导航项（卡片或「标准应用程序」分组标题）。</summary>
    public INavItem? SelectedItem => _selectedItem;

    public AppCardViewModel? SelectedCard => _selectedItem as AppCardViewModel;

    /// <summary>选中的是不是某个沙盒的「标准应用程序」标题行（标准视图）。</summary>
    public bool IsStandardHeaderSelected => _selectedItem is StandardGroupItem;

    /// <summary>选中的是不是「标准应用程序」文件夹块（紧凑视图）。</summary>
    public bool IsFolderSelected => _selectedItem is StandardFolderItem;

    /// <summary>状态/调试显示用：卡片名，或「标准应用程序 · 沙盒名」。</summary>
    public string? SelectedLabel => _selectedItem?.NavTitle;

    public RailItemViewModel? SelectedRail => _selectedRail;

    public int Columns => _columns;

    /// <summary>
    /// 卡片布局指标（标准 / 紧凑共用一套绑定）：切换视图只改这一个对象，
    /// 已生成的卡片由绑定自动重排，不重建集合、不换模板。
    /// </summary>
    public CardMetrics Metrics { get; } = new();

    /// <summary>当前卡片视图，输出给调试通道与报告："standard" / "compact"。</summary>
    public string ViewMode => Metrics.IsCompact ? "compact" : "standard";

    /// <summary>切换紧凑视图（View 负责随后的布局重算 / 图标重算 / 滚动到选中卡）。</summary>
    public void ToggleView() => SetView(!Metrics.IsCompact);

    /// <summary>设置视图；幂等，返回是否真的发生了变化。</summary>
    public bool SetView(bool compact)
    {
        if (!Metrics.Apply(compact))
        {
            return false;
        }

        Raise(nameof(ViewMode));
        Raise(nameof(IsCompactView));

        if (!compact)
        {
            CloseFolder(restoreSelection: false);
        }

        ApplyViewToSections();

        // 视图换了，「卡片可见性」的判定规则也变了（标准视图按折叠态过滤，紧凑视图一律可见），
        // 必须重跑一次 ApplyFilter，否则弹窗里的卡片还停在标准视图的 Collapsed 状态。
        ApplyFilter();

        // 记住选择（下次启动沿用），跟 SelectedSandbox 一个待遇
        _settings.CardView = compact ? "compact" : "standard";
        _settings.Save();

        return true;
    }

    /// <summary>把当前视图模式同步到每个沙盒（紧凑视图显示文件夹块、隐藏内联标准应用列表）。</summary>
    public void ApplyViewToSections()
    {
        var compact = Metrics.IsCompact;
        foreach (var section in _sections)
        {
            section.IsCompactView = compact;
            section.Folder.IsVisible = compact && section.HasStandardApps;
        }

        OnPropertyChanged(nameof(IsCompactView));
    }

    /// <summary>当前是否紧凑视图（XAML 里用来切换「标题行 / 文件夹块 / 内联标准应用」的可见性）。</summary>
    public bool IsCompactView => Metrics.IsCompact;

    /// <summary>按名字设置视图（"compact" / "standard"）；无法识别时返回 false 且不改动。</summary>
    public bool SetView(string mode) => mode.Trim().ToLowerInvariant() switch
    {
        "compact" => SetView(true),
        "standard" => SetView(false),
        _ => false,
    };

    /// <summary>图标服务（View 按视口懒加载时使用；扫描阶段不再逐张 await 图标）。</summary>
    public IconService Icons => _icons;

    /// <summary>最近一次扫描时生效的沙盒根目录模板（FileRootPath）。</summary>
    public string SandboxRootTemplate { get; private set; } = string.Empty;

    /// <summary>根目录来源：custom（设置覆盖）/ ini / default。</summary>
    public string SandboxRootSource { get; private set; } = "default";

    /// <summary>读到的 Sandboxie.ini 路径。</summary>
    public string SandboxIniPath { get; private set; } = string.Empty;

    // ---------------------------------------------------------------- 加载

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            var rootOverride = _settings.SandboxRootPath;
            var config = await Task.Run(
                () => SandboxieConfig.Load(rootPathOverride: rootOverride),
                cancellationToken).ConfigureAwait(true);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            SandboxRootTemplate = config.FileRootPathTemplate;
            SandboxRootSource = config.RootPathSource;
            SandboxIniPath = config.IniPath;
            OnPropertyChanged(nameof(SandboxRootTemplate));
            OnPropertyChanged(nameof(SandboxRootSource));
            OnPropertyChanged(nameof(SandboxIniPath));

            _sections.Clear();
            Sections.Clear();
            RailItems.Clear();

            var allItem = RailItemViewModel.CreateAll();
            RailItems.Add(allItem);

            var byName = new Dictionary<string, SandboxSectionViewModel>(StringComparer.OrdinalIgnoreCase);
            foreach (var box in config.Boxes)
            {
                var display = box.DisplayGroup.Length > 0 ? box.DisplayGroup + " / " + box.Name : box.Name;
                var section = new SandboxSectionViewModel(box, display) { Metrics = Metrics };
                _sections.Add(section);
                Sections.Add(section);
                byName[box.Name] = section;
                RailItems.Add(RailItemViewModel.CreateFor(section));
            }

            var restore = _settings.SelectedSandbox is { } name && byName.TryGetValue(name, out var found)
                ? RailItems.FirstOrDefault(r => ReferenceEquals(r.Section, found))
                : null;

            SelectRail(restore ?? allItem, applyFilter: false);
            ApplyViewToSections();
            ApplyFilter();

            var scans = _sections.Select(section => ScanSectionAsync(section, cancellationToken)).ToList();
            await Task.WhenAll(scans).ConfigureAwait(true);

            if (!cancellationToken.IsCancellationRequested)
            {
                ApplyFilter();
                UpdateRailCounts();
                RebuildCapturePlan();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            HintRequested?.Invoke(this, "扫描失败：" + ex.Message);
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                IsBusy = false;
            }
        }
    }

    private async Task ScanSectionAsync(SandboxSectionViewModel section, CancellationToken cancellationToken)
    {
        if (!section.Box.RootExists)
        {
            return;
        }

        List<DiscoveredApp> apps;
        try
        {
            var candidates = await Task.Run(
                () => SandboxAppDiscoverer.EnumerateCandidates(section.Box, cancellationToken),
                cancellationToken).ConfigureAwait(true);

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            apps = SafeMode.Has("demo")
                ? BuildDemoApps(section.Box)
                : await _sta.InvokeAsync(() => SandboxAppDiscoverer.BuildApps(section.Box, candidates, _reader, cancellationToken)).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch
        {
            return;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        // 标准应用优先占位，沙盒里发现的 Windows 自带工具并入并去重
        var seenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var standard in StandardApps.Create(section.Box))
        {
            if (seenTargets.Add(standard.TargetPath))
            {
                section.StandardApps.Add(new AppCardViewModel(standard, section.SandboxDisplay, Metrics));
            }
        }

        foreach (var app in apps)
        {
            if (StandardApps.IsStandardTarget(app.TargetPath))
            {
                if (seenTargets.Add(app.TargetPath))
                {
                    section.StandardApps.Add(new AppCardViewModel(app with { IsStandard = true }, section.SandboxDisplay, Metrics));
                }

                continue;
            }

            section.AllApps.Add(new AppCardViewModel(app, section.SandboxDisplay, Metrics));
        }

        section.RebuildItems();
        section.NotifyContentChanged();
        ApplyViewToSections();
        ApplyFilter();
        UpdateRailCounts();
        RebuildCapturePlan();
        // 图标不在这里抽：卡片先以字母占位出屏，图标由 View 按滚动视口懒加载
        // （IconService.Request 入队 + 单线程泵），扫描不再等任何一张图标。
    }

    /// <summary>
    /// 预热：加载完成后把所有卡片一次性布局出来（此时还在「扫描中」进度条阶段），
    /// 避免用户第一次切到「全部」时才集中生成大量控件而卡顿。
    /// </summary>
    public void WarmUp()
    {
        foreach (var section in _sections)
        {
            section.IsVisible = true;
            foreach (var card in section.AllApps)
            {
                card.IsVisible = true;
            }

            foreach (var card in section.StandardApps)
            {
                card.IsVisible = section.IsStandardExpanded;
            }
        }
    }

    // ---------------------------------------------------------------- 左栏 / 筛选

    public void SelectRail(RailItemViewModel item, bool applyFilter = true)
    {
        foreach (var rail in RailItems)
        {
            rail.IsSelected = ReferenceEquals(rail, item);
        }

        _selectedRail = item;
        _settings.SelectedSandbox = item.Section?.Name;
        _settings.Save();

        if (applyFilter)
        {
            ApplyFilter();
            SelectFirstVisible();
        }

        InvalidateLayout();
    }

    public void ToggleStandard(SandboxSectionViewModel section)
    {
        _autoExpanded.Remove(section);
        section.IsStandardExpanded = !section.IsStandardExpanded;
        ApplyFilter();

        // 光标留在标题行上：连续按 Space / Enter 可以来回折叠
        Select(section.Header, scroll: true);
    }

    public void ApplyFilter()
    {
        var query = _filterText.Trim();

        foreach (var section in _sections)
        {
            var normalVisible = 0;
            foreach (var card in section.AllApps)
            {
                var match = query.Length == 0 || card.Matches(query);
                card.IsVisible = match;
                if (match)
                {
                    normalVisible++;
                }
            }

            section.VisibleAppCount = normalVisible;

            var standardMatched = 0;
            foreach (var card in section.StandardApps)
            {
                if (query.Length == 0 || card.Matches(query))
                {
                    standardMatched++;
                }
            }

            // 搜索命中标准应用时自动展开，避免「搜到了却看不见」；清空搜索后收回
            if (query.Length > 0 && standardMatched > 0 && !section.IsStandardExpanded)
            {
                section.IsStandardExpanded = true;
                _autoExpanded.Add(section);
            }

            if (!Metrics.IsCompact)
            {
                // 标准视图：内联列表按筛选词过滤
                foreach (var card in section.StandardApps)
                {
                    var match = query.Length == 0 || card.Matches(query);
                    card.IsVisible = match && section.IsStandardExpanded;
                }
            }
            else
            {
                // 紧凑视图：标准应用只在文件夹弹窗里出现，保持可见（数量用于文件夹角标）
                foreach (var card in section.StandardApps)
                {
                    card.IsVisible = true;
                }
            }

            section.VisibleStandardCount = standardMatched;
            section.NotifyContentChanged();

            if (query.Length == 0 && _autoExpanded.Remove(section))
            {
                section.IsStandardExpanded = false;
            }

            // 紧凑视图：文件夹块只在「这一组还有命中内容」时出现，筛出空组时别单独留个文件夹
            if (Metrics.IsCompact)
            {
                section.Folder.IsVisible = section.HasStandardApps &&
                                           (query.Length == 0 || normalVisible > 0 || standardMatched > 0);
            }

            var railOk = _selectedRail is null || _selectedRail.IsAll || ReferenceEquals(_selectedRail.Section, section);
            var hasContent = normalVisible > 0 ||
                             (Metrics.IsCompact ? section.Folder.IsVisible : section.HasStandardApps);
            section.IsVisible = railOk && (hasContent || _settings.ShowEmptySandboxes);
        }

        BuildRows();

        if (_selectedItem is not null && !_positions.ContainsKey(_selectedItem))
        {
            _selectedItem.IsSelected = false;
            _selectedItem = null;
            RefreshSelectionProperties();
        }

        Raise(nameof(HasVisibleCards));
        UpdateRailCounts();
        InvalidateLayout();
    }

    private void UpdateRailCounts()
    {
        var total = 0;
        foreach (var rail in RailItems)
        {
            if (rail.IsAll)
            {
                total = _sections.Sum(s => s.AllApps.Count);
                rail.CountText = total.ToString();
            }
            else if (rail.Section is not null)
            {
                rail.CountText = rail.Section.AllApps.Count.ToString();
            }
        }
    }

    // ---------------------------------------------------------------- 网格导航

    /// <summary>列数变化时（窗口缩放）重建行模型，光标位置尽量保持。</summary>
    public void SetColumns(int columns)
    {
        columns = Math.Max(1, columns);
        if (columns == _columns)
        {
            return;
        }

        _columns = columns;
        BuildRows();
    }

    /// <summary>
    /// 用 View 实测出来的「视觉行」替换行模型。
    /// 这是真正的布局：紧凑视图里两个沙盒块并排时，同一条视觉行的卡片来自不同块，
    /// 上下移动靠各项的水平中心挑最近的列，不会串行。
    /// </summary>
    public void ApplyVisualLayout(IReadOnlyList<VisualRow> visualRows)
    {
        _rows.Clear();
        _positions.Clear();
        _centers.Clear();

        foreach (var visual in visualRows)
        {
            if (visual.Items.Count == 0)
            {
                continue;
            }

            var row = new NavRow();
            var centers = new List<double>(visual.Items.Count);
            for (var i = 0; i < visual.Items.Count; i++)
            {
                _positions[visual.Items[i]] = (_rows.Count, i);
                row.Items.Add(visual.Items[i]);
                centers.Add(i < visual.CenterX.Count ? visual.CenterX[i] : i);
            }

            _rows.Add(row);
            _centers.Add(centers);
        }

        if (_selectedItem is not null && !_positions.ContainsKey(_selectedItem))
        {
            // 文件夹弹窗刚打开：光标从文件夹块落到弹窗里的第一项
            if (IsFolderOpen && _selectedItem is StandardFolderItem && _rows.Count > 0)
            {
                Select(_rows[0].Items[0], scroll: false);
                Raise(nameof(HasVisibleCards));
                return;
            }

            _selectedItem.IsSelected = false;
            _selectedItem = null;
            RefreshSelectionProperties();
        }

        Raise(nameof(HasVisibleCards));
    }

    private void BuildRows()
    {
        _rows.Clear();
        _positions.Clear();
        _centers.Clear();

        foreach (var section in _sections)
        {
            if (!section.IsVisible)
            {
                continue;
            }

            AppendRows(section, section.AllApps);

            if (section.HasStandardApps)
            {
                // 标题行独占一行：方向键能从卡片行下移到这里，Enter / Space 展开收起
                var headerRow = new NavRow();
                _positions[section.Header] = (_rows.Count, 0);
                headerRow.Items.Add(section.Header);
                _rows.Add(headerRow);
                _centers.Add(new List<double> { 0 });
            }

            if (section.IsStandardExpanded)
            {
                AppendRows(section, section.StandardApps);
            }
        }
    }

    private void AppendRows(SandboxSectionViewModel section, IReadOnlyList<AppCardViewModel> cards)
    {
        NavRow? row = null;
        List<double>? centers = null;
        foreach (var card in cards)
        {
            if (!card.IsVisible)
            {
                continue;
            }

            if (row is null || row.Items.Count >= _columns)
            {
                row = new NavRow();
                centers = new List<double>();
                _rows.Add(row);
                _centers.Add(centers);
            }

            _positions[card] = (_rows.Count - 1, row.Items.Count);
            row.Items.Add(card);
            centers!.Add(row.Items.Count - 1);
        }
    }

    /// <summary>方向键移动：dy 走行、dx 走列，跨分组/跨行时按真实布局取最近的卡片。</summary>
    public void MoveSelection(int dx, int dy)
    {
        if (_rows.Count == 0)
        {
            return;
        }

        if (_selectedItem is null || !_positions.TryGetValue(_selectedItem, out var position))
        {
            Select(FirstPreferredItem(), scroll: true);
            return;
        }

        var row = position.Row;
        var column = position.Column;
        if (SafeMode.Has("trace"))
        {
            Diagnostics.Write($"move: dx={dx} dy={dy} from=({row},{column}) '{_selectedItem.NavTitle}'");
        }

        if (dy != 0)
        {
            var targetRow = Math.Clamp(row + dy, 0, _rows.Count - 1);
            if (targetRow == row)
            {
                return;
            }

            // 目标行列数可能和我这一行不同（并排的沙盒块、标题行只有一项），
            // 按水平中心挑最近的一列，视觉上就是「正下方的那一张」。
            var centerX = CenterX(row, column);
            row = targetRow;
            column = NearestColumn(row, centerX);
        }

        if (dx != 0)
        {
            var targetColumn = column + dx;
            if (targetColumn < 0 || targetColumn >= _rows[row].Items.Count)
            {
                return;
            }

            column = targetColumn;
        }

        Select(_rows[row].Items[column], scroll: true);
    }

    public void MovePage(int direction)
    {
        if (_rows.Count == 0 || _selectedItem is null || !_positions.TryGetValue(_selectedItem, out var position))
        {
            return;
        }

        var targetRow = Math.Clamp(position.Row + direction, 0, _rows.Count - 1);
        var column = NearestColumn(targetRow, CenterX(position.Row, position.Column));
        Select(_rows[targetRow].Items[column], scroll: true);
    }

    public void MoveToEdge(bool end)
    {
        if (_rows.Count == 0)
        {
            return;
        }

        if (end)
        {
            var lastRow = _rows[^1];
            Select(lastRow.Items[^1], scroll: true);
        }
        else
        {
            Select(FirstPreferredItem(), scroll: true);
        }
    }

    private void SelectFirstVisible()
    {
        if (_rows.Count == 0)
        {
            if (_selectedItem is not null)
            {
                _selectedItem.IsSelected = false;
                _selectedItem = null;
                RefreshSelectionProperties();
            }

            return;
        }

        if (_selectedItem is not null && _positions.ContainsKey(_selectedItem))
        {
            return;
        }

        Select(FirstPreferredItem(), scroll: false);
    }

    /// <summary>
    /// 默认落点：优先第一张卡片；只有标题行（某段没有普通应用）时才落在标题行上，
    /// 否则一打开面板光标停在「标准应用程序」上，Enter 会变成展开而不是启动应用。
    /// </summary>
    private INavItem FirstPreferredItem()
    {
        foreach (var row in _rows)
        {
            foreach (var item in row.Items)
            {
                if (item is AppCardViewModel)
                {
                    return item;
                }
            }
        }

        return _rows[0].Items[0];
    }

    /// <summary>某行某一项的水平中心；没有实测数据时退回列号（保证不抛异常）。</summary>
    private double CenterX(int row, int column)
    {
        if (row < 0 || row >= _centers.Count)
        {
            return column;
        }

        var centers = _centers[row];
        return column >= 0 && column < centers.Count ? centers[column] : column;
    }

    /// <summary>在指定行里挑水平中心离 <paramref name="centerX"/> 最近的一项。</summary>
    private int NearestColumn(int row, double centerX)
    {
        var items = _rows[row].Items;
        if (items.Count <= 1 || row >= _centers.Count)
        {
            return 0;
        }

        var centers = _centers[row];
        var best = 0;
        var bestDistance = double.MaxValue;
        for (var i = 0; i < items.Count && i < centers.Count; i++)
        {
            var distance = Math.Abs(centers[i] - centerX);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }

    /// <summary>当前可见卡片的扁平顺序（按行优先），调试与验证用。</summary>
    public IReadOnlyList<AppCardViewModel> EnumerateVisibleCards() =>
        _rows.SelectMany(row => row.Items).OfType<AppCardViewModel>().ToList();

    /// <summary>调试用：当前行模型快照（每行的项名 + 水平中心）。</summary>
    public IReadOnlyList<object> DescribeRows()
    {
        var rows = new List<object>(_rows.Count);
        for (var i = 0; i < _rows.Count; i++)
        {
            var rowIndex = i;
            rows.Add(new
            {
                row = rowIndex,
                items = _rows[rowIndex].Items.Select((item, index) => new
                {
                    name = item.NavTitle,
                    x = rowIndex < _centers.Count && index < _centers[rowIndex].Count
                        ? Math.Round(_centers[rowIndex][index], 1)
                        : 0,
                }),
            });
        }

        return rows;
    }

    /// <summary>当前可见的导航项（卡片 + 标准应用分组标题），按行优先。</summary>
    public IReadOnlyList<INavItem> EnumerateVisibleItems() =>
        _rows.SelectMany(row => row.Items).ToList();

    /// <summary>按名称/序号选中某张可见卡片（不启动）。</summary>
    public bool TrySelect(string key, out AppCardViewModel? card)
    {
        var cards = EnumerateVisibleCards();
        card = null;

        if (cards.Count == 0)
        {
            return false;
        }

        if (int.TryParse(key, out var index))
        {
            if (index < 0 || index >= cards.Count)
            {
                return false;
            }

            card = cards[index];
        }
        else
        {
            card = cards.FirstOrDefault(c => string.Equals(c.Name, key, StringComparison.CurrentCultureIgnoreCase))
                   ?? cards.FirstOrDefault(c => c.Name.Contains(key, StringComparison.CurrentCultureIgnoreCase));
        }

        if (card is null)
        {
            return false;
        }

        Select(card, scroll: true);
        return true;
    }

    public void SelectCard(AppCardViewModel card, bool scroll) => Select(card, scroll);

    private void Select(INavItem item, bool scroll)
    {
        if (ReferenceEquals(_selectedItem, item))
        {
            if (scroll)
            {
                SelectionMoved?.Invoke(this, item);
            }

            return;
        }

        if (_selectedItem is not null)
        {
            _selectedItem.IsSelected = false;
        }

        _selectedItem = item;
        item.IsSelected = true;
        RefreshSelectionProperties();

        if (scroll)
        {
            SelectionMoved?.Invoke(this, item);
        }
    }

    private void RefreshSelectionProperties()
    {
        Raise(nameof(SelectedItem));
        Raise(nameof(SelectedCard));
        Raise(nameof(SelectedLabel));
        Raise(nameof(IsStandardHeaderSelected));
    }

    // ---------------------------------------------------------------- 启动 / 运行状态

    /// <summary>Enter / Space：卡片＝启动；「标准应用程序」标题行＝展开/收起。</summary>
    public void ActivateSelected()
    {
        switch (_selectedItem)
        {
            case StandardFolderItem folder:
                OpenFolder(folder);
                return;

            case StandardGroupItem header:
                ToggleStandard(header.Section);
                return;

            case AppCardViewModel card:
                Launch(card);
                return;
        }

        LaunchSelected();
    }

    public void LaunchSelected()
    {
        var card = (_selectedItem as AppCardViewModel)
                   ?? _rows.FirstOrDefault()?.Items.OfType<AppCardViewModel>().FirstOrDefault();
        if (card is not null)
        {
            Launch(card);
        }
    }

    public void Launch(AppCardViewModel card)
    {
        var key = (card.App.SandboxName, card.ProcessName);
        _pendingLaunch[key] = (DateTime.UtcNow, CapturePids(card.ProcessName));

        if (!AppLauncher.TryLaunch(card.App, out var error))
        {
            _pendingLaunch.Remove(key);
            HintRequested?.Invoke(this, "启动失败：" + error);
            return;
        }

        card.IsRunning = true;

        if (_settings.HideOnLaunch)
        {
            HideRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>后台线程用：采样一次「哪个程序在哪个沙盒里跑着」。输入来自 UI 线程重建的不可变快照。</summary>
    public RunningSnapshot CaptureRunning()
    {
        var plan = _capturePlan;
        return ProcessMonitor.Capture(plan.Boxes, plan.Names);
    }

    /// <summary>
    /// 把一次进程采样映射到卡片：
    /// 普通应用看镜像路径归属到哪个沙盒；系统自带工具（目标在宿主机上）只看我们自己启动的那些 pid。
    /// </summary>
    public void ApplyRunningSnapshot(RunningSnapshot snapshot)
    {
        ReconcileLaunches(snapshot);

        foreach (var section in _sections)
        {
            snapshot.NamesByBox.TryGetValue(section.Box.Name, out var runningInBox);

            foreach (var card in section.AllApps)
            {
                card.IsRunning = runningInBox is not null && runningInBox.Contains(card.ProcessName);
            }

            foreach (var card in section.StandardApps)
            {
                card.IsRunning = IsLaunchedAlive(section.Box.Name, card.ProcessName, snapshot);
            }
        }
    }

    /// <summary>把「刚启动」时的基线 pid 与当前 pid 对比，认领新出现的那个进程。</summary>
    private void ReconcileLaunches(RunningSnapshot snapshot)
    {
        if (_pendingLaunch.Count > 0)
        {
            var now = DateTime.UtcNow;
            foreach (var (key, pending) in _pendingLaunch.ToList())
            {
                if (snapshot.PidsByName.TryGetValue(key.Process, out var alive))
                {
                    var fresh = alive.Where(pid => !pending.Baseline.Contains(pid)).ToList();
                    if (fresh.Count > 0)
                    {
                        if (!_launchedPids.TryGetValue(key, out var tracked))
                        {
                            tracked = new HashSet<int>();
                            _launchedPids[key] = tracked;
                        }

                        foreach (var pid in fresh)
                        {
                            tracked.Add(pid);
                        }

                        _pendingLaunch.Remove(key);
                        continue;
                    }
                }

                if (now - pending.At > LaunchClaimWindow)
                {
                    _pendingLaunch.Remove(key);
                }
            }
        }

        if (_launchedPids.Count == 0)
        {
            return;
        }

        foreach (var (key, tracked) in _launchedPids.ToList())
        {
            if (snapshot.PidsByName.TryGetValue(key.Process, out var alive))
            {
                tracked.IntersectWith(alive);
            }
            else
            {
                tracked.Clear();
            }

            if (tracked.Count == 0)
            {
                _launchedPids.Remove(key);
            }
        }
    }

    private bool IsLaunchedAlive(string box, string process, RunningSnapshot snapshot) =>
        _launchedPids.TryGetValue((box, process), out var tracked) &&
        snapshot.PidsByName.TryGetValue(process, out var alive) &&
        tracked.Overlaps(alive);

    private static HashSet<int> CapturePids(string processName)
    {
        var pids = new HashSet<int>();
        try
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    pids.Add(process.Id);
                }
            }
        }
        catch
        {
            // 枚举失败就当作没有基线
        }

        return pids;
    }

    /// <summary>采样输入只保留卡片实际关心的进程名，避免每轮都去开几百个进程。</summary>
    private void RebuildCapturePlan()
    {
        var boxes = new List<SandboxBox>(_sections.Count);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var section in _sections)
        {
            boxes.Add(section.Box);

            foreach (var card in section.AllApps)
            {
                if (card.ProcessName.Length > 0)
                {
                    names.Add(card.ProcessName);
                }
            }

            foreach (var card in section.StandardApps)
            {
                if (card.ProcessName.Length > 0)
                {
                    names.Add(card.ProcessName);
                }
            }
        }

        _capturePlan = new CapturePlan(boxes, names);
    }

    private sealed record CapturePlan(IReadOnlyList<SandboxBox> Boxes, IReadOnlyCollection<string> Names);

    // ---------------------------------------------------------------- 演示数据

    private static List<DiscoveredApp> BuildDemoApps(SandboxBox box)
    {
        var catalog = new (string Name, string Target)[]
        {
            ("记事本", @"C:\Windows\System32\notepad.exe"),
            ("计算器", @"C:\Windows\System32\calc.exe"),
            ("画图", @"C:\Windows\System32\mspaint.exe"),
            ("命令提示符", @"C:\Windows\System32\cmd.exe"),
            ("Windows PowerShell", @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe"),
            ("资源管理器", @"C:\Windows\explorer.exe"),
            ("注册表编辑器", @"C:\Windows\regedit.exe"),
            ("系统信息", @"C:\Windows\System32\msinfo32.exe"),
            ("字符映射表", @"C:\Windows\System32\charmap.exe"),
            ("磁盘清理", @"C:\Windows\System32\cleanmgr.exe"),
            ("远程桌面", @"C:\Windows\System32\mstsc.exe"),
            ("任务管理器", @"C:\Windows\System32\Taskmgr.exe"),
        };

        var count = Math.Clamp(box.Name.Length % 9 + 3, 3, catalog.Length);
        var apps = new List<DiscoveredApp>(count);
        for (var i = 0; i < count; i++)
        {
            var item = catalog[i];
            apps.Add(new DiscoveredApp(
                box.Name,
                item.Name,
                item.Target,
                string.Empty,
                Path.GetDirectoryName(item.Target) ?? string.Empty,
                box.RootPath,
                AppSourceKind.Desktop,
                100 - i));
        }

        return apps;
    }

    private void Raise(string propertyName) => OnPropertyChanged(propertyName);

    private sealed class NavRow
    {
        /// <summary>这一行里的导航项：通常全是卡片；「标准应用程序」标题行只有一项。</summary>
        public List<INavItem> Items { get; } = new();
    }
}
