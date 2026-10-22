using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace ThemeLab;

/// <summary>
/// ThemeLab 样例页：启动卡片 / 左栏项 / 筛选框 / 图标按钮 / 圆圈问号 / 细滚动条。
/// 卡片在代码里生成，但样式全部通过 DynamicResource 取 Fluent.xaml 的键，和真实 App 的用法一致。
/// </summary>
public partial class LabPage : UserControl
{
    private static readonly (string Name, string Path)[] Apps =
    {
        ("记事本", @"C:\Sandbox\User\DefaultBox\drive\C\Program Files\WindowsApps\Microsoft.WindowsNotepad_11.2405.7.0_x64__8wekyb3d8bbwe\Notepad.exe"),
        ("画图", @"C:\Sandbox\User\DefaultBox\drive\C\Program Files\WindowsApps\Microsoft.Paint_11.2404.3.0_x64__8wekyb3d8bbwe\mspaint.exe"),
        ("Visual Studio Code", @"C:\Sandbox\User\DefaultBox\drive\C\Users\User\AppData\Local\Programs\Microsoft VS Code\Code.exe"),
        ("PowerShell 7", @"C:\Sandbox\User\DefaultBox\drive\C\Program Files\PowerShell\7\pwsh.exe"),
        ("7-Zip", @"C:\Sandbox\User\DefaultBox\drive\C\Program Files\7-Zip\7zFM.exe"),
        ("Everything", @"C:\Sandbox\User\DefaultBox\drive\C\Program Files\Everything\Everything.exe"),
        ("Git Bash", @"C:\Sandbox\User\DefaultBox\drive\C\Program Files\Git\git-bash.exe"),
        ("WinRAR", @"C:\Sandbox\User\DefaultBox\drive\C\Program Files\WinRAR\WinRAR.exe"),
        ("Python 3.12", @"C:\Sandbox\User\DefaultBox\drive\C\Users\User\AppData\Local\Programs\Python\Python312\pythonw.exe"),
        ("Node.js", @"C:\Sandbox\User\DefaultBox\drive\C\Program Files\nodejs\node.exe"),
        ("Fiddler Classic", @"C:\Sandbox\User\DefaultBox\drive\C\Program Files (x86)\Fiddler\Fiddler.exe"),
        ("微信", @"C:\Sandbox\User\DefaultBox\drive\C\Program Files\Tencent\WeChat\WeChat.exe"),
        ("网易云音乐", @"C:\Sandbox\User\DefaultBox\drive\C\Program Files (x86)\Netease\CloudMusic\cloudmusic.exe"),
        ("Typora", @"C:\Sandbox\User\DefaultBox\drive\C\Program Files\Typora\Typora.exe"),
        ("Obsidian", @"C:\Sandbox\User\DefaultBox\drive\C\Users\User\AppData\Local\Obsidian\Obsidian.exe"),
        ("Firefox", @"C:\Sandbox\User\DefaultBox\drive\C\Program Files\Mozilla Firefox\firefox.exe"),
        ("Notepad++", @"C:\Sandbox\User\DefaultBox\drive\C\Program Files\Notepad++\notepad++.exe"),
        ("DBeaver", @"C:\Sandbox\User\DefaultBox\drive\C\Program Files\DBeaver\dbeaver.exe"),
        ("Postman", @"C:\Sandbox\User\DefaultBox\drive\C\Users\User\AppData\Local\Postman\Postman.exe"),
        ("WinMerge", @"C:\Sandbox\User\DefaultBox\drive\C\Program Files\WinMerge\WinMergeU.exe"),
        ("Sysinternals Suite", @"C:\Sandbox\User\DefaultBox\drive\C\Tools\Sysinternals\procexp64.exe"),
        ("Sublime Text", @"C:\Sandbox\User\DefaultBox\drive\C\Program Files\Sublime Text\sublime_text.exe"),
        ("Beyond Compare 5", @"C:\Sandbox\User\DefaultBox\drive\C\Program Files\Beyond Compare 5\BCompare.exe"),
        ("Proxifier", @"C:\Sandbox\User\DefaultBox\drive\C\Program Files (x86)\Proxifier\Proxifier.exe"),
        ("qBittorrent", @"C:\Sandbox\User\DefaultBox\drive\C\Program Files\qBittorrent\qbittorrent.exe"),
        ("VLC", @"C:\Sandbox\User\DefaultBox\drive\C\Program Files\VideoLAN\VLC\vlc.exe"),
    };

    private static readonly Color[] TileColors =
    {
        Color.FromRgb(0x00, 0x67, 0xC0), Color.FromRgb(0x7A, 0x3F, 0xF2), Color.FromRgb(0x0F, 0x7B, 0x4F),
        Color.FromRgb(0x2B, 0x57, 0x9A), Color.FromRgb(0xB8, 0x64, 0x1E), Color.FromRgb(0x4A, 0x55, 0x68),
        Color.FromRgb(0x0B, 0x72, 0x85), Color.FromRgb(0x83, 0x38, 0xEC), Color.FromRgb(0xC2, 0x39, 0x34),
        Color.FromRgb(0x1B, 0x6E, 0x8A),
    };

    private readonly List<Button> _cards = new();
    private TextBlock? _sampleCardTitle;

    public LabPage()
    {
        InitializeComponent();
        BuildCards();
    }

    /// <summary>实验室外壳：Mica 近似色随主题切换（仅 Lab 有，不属于 Fluent 契约）。</summary>
    public void ApplyChrome(bool isDark)
    {
        WindowPanel.Background = isDark
            ? new SolidColorBrush(Color.FromArgb(0xB8, 0x1C, 0x1C, 0x1C))
            : new SolidColorBrush(Color.FromArgb(0xB8, 0xF3, 0xF3, 0xF3));
    }

    /// <summary>圆圈问号按钮下方的落点（页面坐标），用于在截图里摆 ToolTip 样例。</summary>
    public Point GetHelpButtonAnchor()
    {
        return HelpButton.TranslatePoint(
            new Point(HelpButton.ActualWidth / 2, HelpButton.ActualHeight + 6), this);
    }

    private void BuildCards()
    {
        CardsPanel.Children.Clear();
        _cards.Clear();

        for (var i = 0; i < Apps.Length; i++)
        {
            var (name, path) = Apps[i];
            var content = CreateCardContent(name, path, i);
            _sampleCardTitle ??= ((Grid)content).Children.OfType<TextBlock>().FirstOrDefault();

            var card = new Button
            {
                Content = content,
                ToolTip = path,
            };
            card.SetResourceReference(StyleProperty, "LauncherCardStyle");

            CardsPanel.Children.Add(card);
            _cards.Add(card);
        }

        CountHint.Text = Apps.Length + " 个应用";
    }

    private static FrameworkElement CreateCardContent(string name, string path, int index)
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var tile = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(TileColors[index % TileColors.Length]),
            Child = new TextBlock
            {
                Text = name.Substring(0, 1),
                Foreground = Brushes.White,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Grid.SetRow(tile, 0);
        grid.Children.Add(tile);

        var title = new TextBlock
        {
            Text = name,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 8, 0, 0),
        };
        title.SetResourceReference(StyleProperty, "TextStyleBodyStrong");
        Grid.SetRow(title, 1);
        grid.Children.Add(title);

        var caption = new TextBlock
        {
            Text = path,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 2, 0, 0),
        };
        caption.SetResourceReference(StyleProperty, "TextStyleCaption");
        Grid.SetRow(caption, 2);
        grid.Children.Add(caption);

        return grid;
    }

    /// <summary>诊断用：打印几个关键元素的实际画刷颜色，验证动态资源是否随主题更新。</summary>
    public string ProbeColors()
    {
        static string C(Brush? brush) =>
            brush is SolidColorBrush s ? s.Color.ToString() : brush?.GetType().Name ?? "null";

        return "TitleText=" + C(TitleText.Foreground)
               + " CardTitle=" + C(_sampleCardTitle?.Foreground)
               + " RailFg=" + C(RailAll.Foreground)
               + " PanelBg=" + C(WindowPanel.Background)
               + " LayerFill=" + C((Brush?)Application.Current.TryFindResource("LayerFillBrush"))
               + " TextPrimary=" + C((Brush?)Application.Current.TryFindResource("TextPrimaryBrush"));
    }

    /// <summary>给筛选框填一段文字（检查 TextBox 模板里文字基线 / 光标位置）。</summary>
    public void SetFilterText(string text)
    {
        FilterBox.Text = text;
        FilterBox.CaretIndex = text.Length;
        FilterPlaceholder.Visibility = string.IsNullOrEmpty(text) ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>诊断用：滚动卡片区，验证 ScrollViewerStyle 内部 ScrollBar 模板（PART_Track）接线正常。</summary>
    public string ProbeScroll(double offset)
    {
        CardsScroller.ScrollToVerticalOffset(offset);
        CardsScroller.UpdateLayout();

        var bar = CardsScroller.Template?.FindName("PART_VerticalScrollBar", CardsScroller) as ScrollBar;
        return "offset=" + CardsScroller.VerticalOffset.ToString("0.#")
               + " barValue=" + bar?.Value.ToString("0.#")
               + " barMax=" + bar?.Maximum.ToString("0.#")
               + " barVisibility=" + bar?.Visibility;
    }

    /// <summary>给截图摆出 hover / 键盘聚焦 / 输入框聚焦三种「用户态」，方便一眼验收。</summary>
    public void MarkDemoStates()
    {
        foreach (var card in _cards)
        {
            card.ApplyTemplate();
        }

        if (_cards.Count > 1 && _cards[1].Template?.FindName("Card", _cards[1]) is Border hoverCard)
        {
            hoverCard.SetResourceReference(Border.BackgroundProperty, "LayerFillHoverBrush");
            hoverCard.SetResourceReference(Border.BorderBrushProperty, "LayerBorderHoverBrush");
        }

        if (_cards.Count > 2 && _cards[2].Template?.FindName("FocusRing", _cards[2]) is Border focusRing)
        {
            focusRing.Visibility = Visibility.Visible;
        }

        FilterBox.ApplyTemplate();
        if (FilterBox.Template?.FindName("Frame", FilterBox) is Border frame)
        {
            frame.SetResourceReference(Border.BorderBrushProperty, "ControlBorderFocusBrush");
        }

        if (FilterBox.Template?.FindName("FocusLine", FilterBox) is Border focusLine)
        {
            focusLine.Visibility = Visibility.Visible;
        }
    }
}