namespace Custodian.App.ViewModels;

/// <summary>
/// 键盘导航模型里的一个可选中项。目前两类：应用卡片（<see cref="AppCardViewModel"/>）与
/// 「标准应用程序」分组标题（<see cref="StandardGroupItem"/>）。
/// 行模型只认这个接口，因此分组标题和卡片一样能被方向键落到、被选中环高亮。
/// </summary>
public interface INavItem
{
    /// <summary>调试/日志用的名字。</summary>
    string NavTitle { get; }

    bool IsSelected { get; set; }
}

/// <summary>「标准应用程序」分组标题行：选它按 Enter/Space 就展开/收起该分组。</summary>
public sealed class StandardGroupItem : ObservableObject, INavItem
{
    private bool _isSelected;

    public StandardGroupItem(SandboxSectionViewModel section) => Section = section;

    public SandboxSectionViewModel Section { get; }

    public string NavTitle => "标准应用程序 · " + Section.Name;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (Set(ref _isSelected, value))
            {
                RaiseSelectedChanged();
            }
        }
    }
}

/// <summary>
/// View 实测出来的一行导航项（同一视觉行上的卡片 / 分组标题）。
/// <c>CenterX</c> 是各项在行内的水平中心（DIP），上下移动时用它挑「最近的那一列」，
/// 这样紧凑视图里多个沙盒块并排时，从某块往下走不会串到别的块上去。
/// </summary>
/// <param name="Items">按水平位置排好序的导航项。</param>
/// <param name="CenterX">与 <paramref name="Items"/> 一一对应的水平中心。</param>
public sealed record VisualRow(IReadOnlyList<INavItem> Items, IReadOnlyList<double> CenterX);

/// <summary>
/// 紧凑视图里的「标准应用程序」文件夹块：外观是 3×3 的图标预览（类似手机桌面文件夹），
/// 选中后 Enter / Space 打开居中弹窗查看内容；它是该沙盒应用列表的最后一个导航项。
/// </summary>
public sealed class StandardFolderItem : ObservableObject, INavItem
{
    private const int PreviewCount = 4;

    private bool _isSelected;
    private bool _isVisible;

    public StandardFolderItem(SandboxSectionViewModel section) => Section = section;

    public SandboxSectionViewModel Section { get; }

    /// <summary>共享布局指标（模板里的尺寸/对齐都用它，和普通应用卡片同一套）。</summary>
    public CardMetrics Metrics => Section.Metrics!;

    public string NavTitle => "标准应用程序 · " + Section.Name;

    public string Title => "标准应用程序";

    /// <summary>预览用：前 4 个标准应用（2×2，图标可能在懒加载后到位）。</summary>
    public IReadOnlyList<AppCardViewModel> PreviewApps =>
        Section.StandardApps.Count <= PreviewCount
            ? Section.StandardApps
            : Section.StandardApps.Take(PreviewCount).ToList();

    public string CountText => Section.StandardApps.Count.ToString();

    /// <summary>预览网格列数：固定 2×2（和普通应用的图标块一样大，看上去才像同一套卡片）。</summary>
    public int PreviewColumns => 2;

    /// <summary>是否在当前视图里显示（只在紧凑视图显示文件夹块）。</summary>
    public bool IsVisible
    {
        get => _isVisible;
        set => Set(ref _isVisible, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (Set(ref _isSelected, value))
            {
                RaiseSelectedChanged();
            }
        }
    }

    public void NotifyContentChanged()
    {
        OnPropertyChanged(nameof(PreviewApps));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(PreviewColumns));
    }
}
