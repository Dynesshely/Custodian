using System.Collections.ObjectModel;
using Custodian.Core.Sandboxie;

namespace Custodian.App.ViewModels;

/// <summary>一个沙盒在面板中的分组：稳定持有全部卡片，筛选只切换 IsVisible，不重建集合。</summary>
public sealed class SandboxSectionViewModel : ObservableObject
{
    private bool _isVisible = true;
    private bool _isStandardExpanded;
    private bool _isCompactView;
    private int _visibleAppCount;
    private int _visibleStandardCount;

    public SandboxSectionViewModel(SandboxBox box, string sandboxDisplay)
    {
        Box = box;
        SandboxDisplay = sandboxDisplay;
        Header = new StandardGroupItem(this);
        Folder = new StandardFolderItem(this);
    }

    public SandboxBox Box { get; }

    /// <summary>卡片上展示的沙盒名（含分组路径）。</summary>
    public string SandboxDisplay { get; }

    public string Name => Box.Name;

    public string GroupLabel => Box.DisplayGroup;

    public string GroupLabelSuffix => HasGroupLabel ? "· " + GroupLabel : string.Empty;

    public bool HasGroupLabel => GroupLabel.Length > 0;

    /// <summary>「标准应用程序」分组标题行的导航项（能被方向键选中、Enter/Space 展开收起）。</summary>
    public StandardGroupItem Header { get; }

    private double _blockWidth = double.NaN;

    /// <summary>
    /// 该分组块的宽度（View 按实测布局写入）：标准视图＝整行宽（等价纵向堆叠），
    /// 紧凑视图＝按内容算出的块宽，窗口够宽时两个沙盒块可以并排。NaN 表示自适应。
    /// </summary>
    public double BlockWidth
    {
        get => _blockWidth;
        set => Set(ref _blockWidth, value);
    }

    /// <summary>共享布局指标（由 MainViewModel 注入；文件夹块的模板也绑它）。</summary>
    public CardMetrics? Metrics { get; set; }

    /// <summary>紧凑视图里的「标准应用程序」文件夹块（永远排在该沙盒应用列表最后）。</summary>
    public StandardFolderItem Folder { get; }

    /// <summary>
    /// 卡片区的绑定源：普通应用 + 末尾的「标准应用程序」文件夹块。
    /// 标准视图里文件夹块的 IsVisible=false（模板整体 Collapsed，不占位），视觉上仍是原来的「卡片 + 标题行」。
    /// </summary>
    public ObservableCollection<INavItem> Items { get; } = new();

    /// <summary>普通应用（来自沙盒快捷方式），顺序稳定。</summary>
    public List<AppCardViewModel> AllApps { get; } = new();

    /// <summary>标准应用程序，顺序稳定。</summary>
    public List<AppCardViewModel> StandardApps { get; } = new();

    public bool IsVisible
    {
        get => _isVisible;
        set => Set(ref _isVisible, value);
    }

    public bool IsStandardExpanded
    {
        get => _isStandardExpanded;
        set
        {
            if (Set(ref _isStandardExpanded, value))
            {
                OnPropertyChanged(nameof(StandardChevronGlyph));
                OnPropertyChanged(nameof(ShowInlineStandardApps));
            }
        }
    }

    public string StandardChevronGlyph => _isStandardExpanded ? "\uE70D" : "\uE76C";

    public bool HasStandardApps => StandardApps.Count > 0;

    /// <summary>当前是否紧凑视图（由 ViewModel 在切换视图时写入）。</summary>
    public bool IsCompactView
    {
        get => _isCompactView;
        set
        {
            if (Set(ref _isCompactView, value))
            {
                OnPropertyChanged(nameof(ShowHeaderRow));
                OnPropertyChanged(nameof(ShowInlineStandardApps));
            }
        }
    }

    /// <summary>标准视图才显示「标准应用程序」标题行（紧凑视图用文件夹块代替）。</summary>
    public bool ShowHeaderRow => HasStandardApps && !_isCompactView;

    /// <summary>标准视图展开后的内联标准应用列表（紧凑视图里内容在文件夹弹窗里）。</summary>
    public bool ShowInlineStandardApps => !_isCompactView && _isStandardExpanded;

    public bool HasAnyContent => AllApps.Count > 0 || StandardApps.Count > 0;

    public int VisibleAppCount
    {
        get => _visibleAppCount;
        set
        {
            if (Set(ref _visibleAppCount, value))
            {
                OnPropertyChanged(nameof(CountText));
            }
        }
    }

    public int VisibleStandardCount
    {
        get => _visibleStandardCount;
        set
        {
            if (Set(ref _visibleStandardCount, value))
            {
                OnPropertyChanged(nameof(StandardCountText));
            }
        }
    }

    /// <summary>扫描完成后重建卡片区绑定源：普通应用在前，文件夹块固定最后。</summary>
    public void RebuildItems()
    {
        Items.Clear();
        foreach (var app in AllApps)
        {
            Items.Add(app);
        }

        Items.Add(Folder);
        Folder.NotifyContentChanged();
    }

    /// <summary>卡片列表是普通 List（不重建），填充/展开后要手动通知这些计算属性。</summary>
    public void NotifyContentChanged()
    {
        Folder.NotifyContentChanged();
        OnPropertyChanged(nameof(HasStandardApps));
        OnPropertyChanged(nameof(ShowHeaderRow));
        OnPropertyChanged(nameof(ShowInlineStandardApps));
        OnPropertyChanged(nameof(HasAnyContent));
        OnPropertyChanged(nameof(VisibleAppCount));
        OnPropertyChanged(nameof(VisibleStandardCount));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(StandardCountText));
    }

    public string CountText => _visibleAppCount.ToString();

    public string StandardCountText => _visibleStandardCount.ToString();
}
