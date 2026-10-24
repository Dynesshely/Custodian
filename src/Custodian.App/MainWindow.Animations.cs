using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Custodian.App;

/// <summary>
/// 窗口与弹窗的过渡动画（全部用缓动，不做线性匀速）。
///
/// 三处：窗口出现/收起、紧凑视图的文件夹弹窗开合、按下 Enter 启动应用时窗口边框的 loading 转圈。
/// 尊重系统「显示动画」开关（<see cref="SystemParameters.ClientAreaAnimation"/>），
/// 也支持 <c>CUSTODIAN_SAFE=anim</c> 一次性关掉，方便截图与排障。
/// </summary>
public partial class MainWindow
{
    private const int WindowIntroMs = 200;
    private const int WindowOutroMs = 160;
    private const int FolderTransitionMs = 170;
    private const int LaunchRingMs = 420;

    /// <summary>一圈的长度（StrokeDashArray 单位 = 描边宽度的倍数）：80 + 1860。</summary>
    private const double LaunchRingCycle = 80 + 1860;

    private static readonly CubicEase EaseOutCurve = new() { EasingMode = EasingMode.EaseOut };
    private static readonly CubicEase EaseInCurve = new() { EasingMode = EasingMode.EaseIn };

    private bool _hiding;
    private bool _launchRingRunning;
    private bool _hideAfterLaunch;

    private static bool AnimationsEnabled => SystemParameters.ClientAreaAnimation && !SafeMode.Has("anim");

    // ------------------------------------------------------------ 窗口出现 / 收起

    private void PlayWindowIntro()
    {
        if (!AnimationsEnabled)
        {
            ResetWindowVisuals();
            return;
        }

        StopWindowAnimations();
        RootShift.Y = 14;
        RootBorder.Opacity = 0;

        var duration = TimeSpan.FromMilliseconds(WindowIntroMs);
        RootShift.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(14, 0, duration) { EasingFunction = EaseOutCurve });
        RootBorder.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, duration) { EasingFunction = EaseOutCurve });
    }

    private void PlayWindowOutro(Action onCompleted)
    {
        if (!AnimationsEnabled || !IsVisible)
        {
            ResetWindowVisuals();
            onCompleted();
            return;
        }

        StopWindowAnimations();

        var duration = TimeSpan.FromMilliseconds(WindowOutroMs);
        var fade = new DoubleAnimation(1, 0, duration) { EasingFunction = EaseInCurve };
        fade.Completed += (_, _) =>
        {
            ResetWindowVisuals();
            onCompleted();
        };

        RootShift.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(0, -10, duration) { EasingFunction = EaseInCurve });
        RootBorder.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private void ResetWindowVisuals()
    {
        StopWindowAnimations();
        RootBorder.Opacity = 1;
        RootShift.Y = 0;
    }

    private void StopWindowAnimations()
    {
        RootBorder.BeginAnimation(UIElement.OpacityProperty, null);
        RootShift.BeginAnimation(TranslateTransform.YProperty, null);
    }

    // ------------------------------------------------------------ 文件夹弹窗

    /// <summary>紧凑视图：弹窗打开/关闭的过渡（面板缩放 + 淡入淡出，遮罩与模糊快照一起淡入）。</summary>
    private void AnimateFolderOverlay(bool open)
    {
        if (!AnimationsEnabled)
        {
            ApplyFolderOverlayState(open);
            return;
        }

        var duration = TimeSpan.FromMilliseconds(open ? FolderTransitionMs : FolderTransitionMs * 0.8);
        FolderOverlay.IsHitTestVisible = open;

        if (open)
        {
            FolderOverlay.Visibility = Visibility.Visible;
            FolderScrim.Opacity = 0;
            BackdropSnapshot.Opacity = 0;
            FolderPanelHost.Opacity = 0;
            FolderPanelScale.ScaleX = 0.94;
            FolderPanelScale.ScaleY = 0.94;

            FolderScrim.BeginAnimation(
                UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, duration) { EasingFunction = EaseOutCurve });
            BackdropSnapshot.BeginAnimation(
                UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, duration) { EasingFunction = EaseOutCurve });
            FolderPanelHost.BeginAnimation(
                UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, duration) { EasingFunction = EaseOutCurve });
            FolderPanelScale.BeginAnimation(
                ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0.94, 1, duration) { EasingFunction = EaseOutCurve });
            FolderPanelScale.BeginAnimation(
                ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.94, 1, duration) { EasingFunction = EaseOutCurve });
            return;
        }

        var fade = new DoubleAnimation(1, 0, duration) { EasingFunction = EaseInCurve };
        fade.Completed += (_, _) => ApplyFolderOverlayState(false);

        FolderScrim.BeginAnimation(UIElement.OpacityProperty, fade);
        BackdropSnapshot.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(1, 0, duration) { EasingFunction = EaseInCurve });
        FolderPanelHost.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(1, 0, duration) { EasingFunction = EaseInCurve });
        FolderPanelScale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1, 0.96, duration) { EasingFunction = EaseInCurve });
        FolderPanelScale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            new DoubleAnimation(1, 0.96, duration) { EasingFunction = EaseInCurve });
    }

    /// <summary>直接把弹窗设到某个稳定状态（动画关闭时 / 动画结束后）。</summary>
    private void ApplyFolderOverlayState(bool open)
    {
        FolderScrim.BeginAnimation(UIElement.OpacityProperty, null);
        BackdropSnapshot.BeginAnimation(UIElement.OpacityProperty, null);
        FolderPanelHost.BeginAnimation(UIElement.OpacityProperty, null);
        FolderPanelScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        FolderPanelScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

        FolderScrim.Opacity = 1;
        BackdropSnapshot.Opacity = 1;
        FolderPanelHost.Opacity = 1;
        FolderPanelScale.ScaleX = 1;
        FolderPanelScale.ScaleY = 1;

        FolderOverlay.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        FolderOverlay.IsHitTestVisible = open;
    }

    // ------------------------------------------------------------ 启动应用的边框 loading

    /// <summary>
    /// 按 Enter 启动应用后：窗口边框上有一段高亮沿边框转一圈（约 0.4s），
    /// 动画结束再交由 <see cref="HidePanel"/> 播收起动画。
    /// </summary>
    private async Task PlayLaunchRingAsync()
    {
        if (!AnimationsEnabled)
        {
            return;
        }

        _launchRingRunning = true;
        LaunchRing.Visibility = Visibility.Visible;
        LaunchRing.StrokeDashOffset = 0;

        var duration = TimeSpan.FromMilliseconds(LaunchRingMs);
        LaunchRing.BeginAnimation(
            Shape.StrokeDashOffsetProperty,
            new DoubleAnimation(0, -LaunchRingCycle, duration)
            {
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            });

        var opacity = new DoubleAnimationUsingKeyFrames();
        opacity.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        opacity.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.2)));
        opacity.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.72)));
        opacity.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
        LaunchRing.BeginAnimation(UIElement.OpacityProperty, opacity);

        await Task.Delay(LaunchRingMs);

        LaunchRing.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
        LaunchRing.BeginAnimation(UIElement.OpacityProperty, null);
        LaunchRing.StrokeDashOffset = 0;
        LaunchRing.Opacity = 0;
        LaunchRing.Visibility = Visibility.Collapsed;
        _launchRingRunning = false;

        if (_hideAfterLaunch)
        {
            _hideAfterLaunch = false;
            HidePanel();
        }
    }

    /// <summary>启动后想收起窗口时：先让边框 loading 播完，再走收起动画。</summary>
    private void RequestHideAfterLaunch()
    {
        if (_launchRingRunning)
        {
            _hideAfterLaunch = true;
            return;
        }

        HidePanel();
    }
}
