using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Custodian.App.Controls;

/// <summary>
/// 圆圈问号：鼠标悬浮时浮现说明。UI 里所有提示统一走这个控件，避免堆无意义文字。
/// 外观是 Fluent 风格的圆形浮动按钮（FAB），默认放在窗口右下角、浮在卡片区之上。
/// </summary>
public sealed class InfoTip : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(InfoTip),
        new PropertyMetadata(string.Empty, OnTextChanged));

    /// <summary>FAB 直径（DIP）。取 36–40 之间，38 在 24 margin 下视觉最稳。</summary>
    private const double Diameter = 38;

    private readonly Border _surface;
    private readonly ToolTip _toolTip;
    private bool _isHovered;
    private bool _isPressed;

    public InfoTip()
    {
        Focusable = false;
        IsTabStop = false;
        Cursor = System.Windows.Input.Cursors.Help;
        Width = Diameter;
        Height = Diameter;

        // 问号单独画在投影层之上，避免 DropShadowEffect 把文字一起栅格化后发虚。
        var glyph = new TextBlock
        {
            Text = "?",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        glyph.SetResourceReference(TextBlock.ForegroundProperty, "IconMonoBrush");
        glyph.SetResourceReference(TextBlock.FontFamilyProperty, "AppFontFamily");

        _surface = new Border
        {
            CornerRadius = new CornerRadius(Diameter / 2),
            BorderThickness = new Thickness(1),
            SnapsToDevicePixels = true,
            Effect = new DropShadowEffect
            {
                BlurRadius = 10,
                ShadowDepth = 2,
                Direction = 270,
                Opacity = 0.2,
                Color = Colors.Black,
            },
        };
        _surface.SetResourceReference(Border.BackgroundProperty, "ControlFillBrush");
        _surface.SetResourceReference(Border.BorderBrushProperty, "ControlBorderBrush");

        var host = new Grid();
        host.Children.Add(_surface);
        host.Children.Add(glyph);
        Content = host;

        MouseEnter += (_, _) => SetHovered(true);
        MouseLeave += (_, _) =>
        {
            SetHovered(false);
            SetPressed(false);
        };
        PreviewMouseLeftButtonDown += (_, _) => SetPressed(true);
        PreviewMouseLeftButtonUp += (_, _) => SetPressed(false);

        _toolTip = new ToolTip
        {
            Placement = System.Windows.Controls.Primitives.PlacementMode.Top,
        };
        _toolTip.SetResourceReference(StyleProperty, "FluentToolTipStyle");
        ToolTipService.SetInitialShowDelay(this, 150);
        ToolTipService.SetShowDuration(this, 30000);
        ToolTip = _toolTip;
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private void SetHovered(bool value)
    {
        if (_isHovered == value)
        {
            return;
        }

        _isHovered = value;
        RefreshSurface();
    }

    private void SetPressed(bool value)
    {
        if (_isPressed == value)
        {
            return;
        }

        _isPressed = value;
        RefreshSurface();
    }

    private void RefreshSurface()
    {
        // 按下用 SubtlePressedBrush：主题里没有 ControlFillPressedBrush，
        // 与工具栏图标按钮的按下态保持一致。
        var key = _isPressed
            ? "SubtlePressedBrush"
            : _isHovered
                ? "ControlFillHoverBrush"
                : "ControlFillBrush";
        _surface.SetResourceReference(Border.BackgroundProperty, key);
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not InfoTip tip)
        {
            return;
        }

        var text = e.NewValue as string ?? string.Empty;
        tip._toolTip.Content = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 320,
            LineHeight = 18,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        };

        AutomationProperties.SetName(tip, text);
    }
}
