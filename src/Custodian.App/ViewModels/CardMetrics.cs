using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Custodian.App.ViewModels;

/// <summary>
/// 卡片布局指标：标准视图与紧凑视图共用同一套 XAML（<c>AppCardTemplate</c>），
/// 切换视图只改这一个对象的属性，已生成的卡片由绑定自动重排
/// —— 不重建集合、不准备第二套模板 / ItemsControl。
/// View 侧（列数测量、PageRows、图标预取）读 <see cref="Step"/> / <see cref="RowHeight"/>，不要硬编码卡片尺寸。
/// </summary>
public sealed class CardMetrics : ObservableObject
{
    /// <summary>标准视图：横向卡片（左图标，右边名称 + 路径）。</summary>
    public const double StandardWidth = 200;
    public const double StandardHeight = 88;
    public const double StandardIconSize = 40;
    public const double StandardTitleFontSize = 13;
    public const double StandardCornerRadius = 8;
    public const double StandardTextWidth = 126;

    /// <summary>紧凑视图：手机桌面式网格（图标居中 + 单行标题，无底色，仅在悬浮/选中时浮出背景）。</summary>
    public const double CompactWidth = 96;
    public const double CompactHeight = 96;
    public const double CompactIconSize = 42;
    public const double CompactTitleFontSize = 12;
    public const double CompactCornerRadius = 10;
    public const double CompactTextWidth = 88;

    /// <summary>这些属性在切换视图时整体变化，统一 Raise 一次。</summary>
    private static readonly string[] MetricPropertyNames =
    {
        nameof(Width), nameof(Height), nameof(Margin), nameof(Step), nameof(RowHeight),
        nameof(IconSize), nameof(IconCornerRadius), nameof(InitialFontSize),
        nameof(TitleFontSize), nameof(TitleMargin), nameof(TitleAlignment),
        nameof(IconAlignment), nameof(ShowPath), nameof(CornerRadius), nameof(CardPadding),
        nameof(CardOrientation), nameof(ContentAlignment), nameof(TextWidth),
        nameof(ShowCardBackground),
    };

    private bool _isCompact;
    private double _width = StandardWidth;
    private double _height = StandardHeight;
    private Thickness _margin = new(4);
    private double _iconSize = StandardIconSize;
    private double _iconCornerRadius = 8;
    private double _initialFontSize = 16;
    private double _titleFontSize = StandardTitleFontSize;
    private Thickness _titleMargin = new(10, 0, 0, 0);
    private TextAlignment _titleAlignment = TextAlignment.Left;
    private HorizontalAlignment _iconAlignment = HorizontalAlignment.Left;
    private bool _showPath = true;
    private double _cornerRadius = StandardCornerRadius;
    private Thickness _cardPadding = new(12);
    private Orientation _cardOrientation = Orientation.Horizontal;
    private HorizontalAlignment _contentAlignment = HorizontalAlignment.Left;
    private double _textWidth = StandardTextWidth;
    private bool _showCardBackground = true;

    /// <summary>当前是否紧凑视图。</summary>
    public bool IsCompact => _isCompact;

    /// <summary>卡片本体宽度（不含外边距）。</summary>
    public double Width
    {
        get => _width;
        private set => Set(ref _width, value);
    }

    /// <summary>卡片本体高度（不含外边距）。</summary>
    public double Height
    {
        get => _height;
        private set => Set(ref _height, value);
    }

    /// <summary>卡片外边距；也决定网格步进。</summary>
    public Thickness Margin
    {
        get => _margin;
        private set => Set(ref _margin, value);
    }

    /// <summary>网格步进（列宽）：卡片宽度 + 左右外边距。标准 208 / 紧凑 104。</summary>
    public double Step => _width + _margin.Left + _margin.Right;

    /// <summary>一行的高度：卡片高度 + 上下外边距。标准 104 / 紧凑 104。</summary>
    public double RowHeight => _height + _margin.Top + _margin.Bottom;

    /// <summary>图标绘制边长。</summary>
    public double IconSize
    {
        get => _iconSize;
        private set => Set(ref _iconSize, value);
    }

    /// <summary>无图标时字母占位块的圆角。</summary>
    public double IconCornerRadius
    {
        get => _iconCornerRadius;
        private set => Set(ref _iconCornerRadius, value);
    }

    /// <summary>字母占位字号。</summary>
    public double InitialFontSize
    {
        get => _initialFontSize;
        private set => Set(ref _initialFontSize, value);
    }

    /// <summary>标题字号。</summary>
    public double TitleFontSize
    {
        get => _titleFontSize;
        private set => Set(ref _titleFontSize, value);
    }

    /// <summary>文字块外边距（标准＝图标右侧间距，紧凑＝图标下方间距）。</summary>
    public Thickness TitleMargin
    {
        get => _titleMargin;
        private set => Set(ref _titleMargin, value);
    }

    /// <summary>标题水平对齐（标准左对齐 / 紧凑居中）。</summary>
    public TextAlignment TitleAlignment
    {
        get => _titleAlignment;
        private set => Set(ref _titleAlignment, value);
    }

    /// <summary>图标水平对齐（标准靠左 / 紧凑居中）。</summary>
    public HorizontalAlignment IconAlignment
    {
        get => _iconAlignment;
        private set => Set(ref _iconAlignment, value);
    }

    /// <summary>是否显示路径文字（紧凑视图隐藏）。</summary>
    public bool ShowPath
    {
        get => _showPath;
        private set => Set(ref _showPath, value);
    }

    /// <summary>选中环 / 运行中绿框的圆角。</summary>
    public double CornerRadius
    {
        get => _cornerRadius;
        private set => Set(ref _cornerRadius, value);
    }

    /// <summary>卡片内容内边距。</summary>
    public Thickness CardPadding
    {
        get => _cardPadding;
        private set => Set(ref _cardPadding, value);
    }

    /// <summary>图标与文字的排布方向：标准＝横向（名称在图标右侧），紧凑＝纵向（图标在上）。</summary>
    public Orientation CardOrientation
    {
        get => _cardOrientation;
        private set => Set(ref _cardOrientation, value);
    }

    /// <summary>内容整体的水平对齐。</summary>
    public HorizontalAlignment ContentAlignment
    {
        get => _contentAlignment;
        private set => Set(ref _contentAlignment, value);
    }

    /// <summary>文字块宽度（横向排布时要用固定宽度，否则长名字会撑出卡片）。</summary>
    public double TextWidth
    {
        get => _textWidth;
        private set => Set(ref _textWidth, value);
    }

    /// <summary>
    /// 是否画卡片底色/描边。紧凑视图不画，只有鼠标悬浮、按下或选中时才浮出背景（选中环是独立叠层）。
    /// </summary>
    public bool ShowCardBackground
    {
        get => _showCardBackground;
        private set => Set(ref _showCardBackground, value);
    }

    /// <summary>切到目标视图；返回是否真的发生了变化（幂等，重复调用不会触发重排）。</summary>
    public bool Apply(bool compact)
    {
        if (_isCompact == compact)
        {
            return false;
        }

        _isCompact = compact;

        if (compact)
        {
            _width = CompactWidth;
            _height = CompactHeight;
            _margin = new Thickness(4);
            _iconSize = CompactIconSize;
            _iconCornerRadius = 10;
            _initialFontSize = 17;
            _titleFontSize = CompactTitleFontSize;
            _titleMargin = new Thickness(0, 5, 0, 0);
            _titleAlignment = TextAlignment.Center;
            _iconAlignment = HorizontalAlignment.Center;
            _showPath = false;
            _cornerRadius = CompactCornerRadius;
            _cardPadding = new Thickness(4, 6, 4, 4);
            _cardOrientation = Orientation.Vertical;
            _contentAlignment = HorizontalAlignment.Center;
            _textWidth = CompactTextWidth;
            _showCardBackground = false;
        }
        else
        {
            _width = StandardWidth;
            _height = StandardHeight;
            _margin = new Thickness(4);
            _iconSize = StandardIconSize;
            _iconCornerRadius = 8;
            _initialFontSize = 16;
            _titleFontSize = StandardTitleFontSize;
            _titleMargin = new Thickness(10, 0, 0, 0);
            _titleAlignment = TextAlignment.Left;
            _iconAlignment = HorizontalAlignment.Left;
            _showPath = true;
            _cornerRadius = StandardCornerRadius;
            _cardPadding = new Thickness(12, 8, 12, 8);
            _cardOrientation = Orientation.Horizontal;
            _contentAlignment = HorizontalAlignment.Left;
            _textWidth = StandardTextWidth;
            _showCardBackground = true;
        }

        OnPropertyChanged(nameof(IsCompact));
        foreach (var name in MetricPropertyNames)
        {
            OnPropertyChanged(name);
        }

        return true;
    }
}
