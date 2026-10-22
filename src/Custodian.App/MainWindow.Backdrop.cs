using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Custodian.App;

/// <summary>
/// 文件夹弹窗的「毛玻璃」背景。
///
/// 以前是给整窗内容挂一个 <c>BlurEffect Radius=18</c>：只要弹窗开着，WPF 每帧都要把整个子树
/// 光栅化到离屏表面（1736×1136 px ≈ 7.9 MB/张，还要多级降采样），反复开关弹窗就不断申请渲染目标，
/// 私有内存会抬到 180 MB 以上的平台，开关瞬间 CPU 也能冲到 1 核以上。
///
/// 现在的做法是「开弹窗时截一次图 → 缩小一半 → 在 CPU 上做两次盒式模糊 → 直接把这张位图铺在遮罩下面」，
/// 同时把真实内容设为 <see cref="Visibility.Hidden"/>（保留布局、跳过绘制）。弹窗期间没有离屏渲染，
/// 位图只有 868×568×4 ≈ 2 MB，开关再多次也不会累积渲染目标。
/// </summary>
public partial class MainWindow
{
    /// <summary>快照缩放：0.5× 已经足够糊，像素只有 1/4。</summary>
    private const double BackdropSnapshotScale = 0.5;

    /// <summary>0.5× 下的模糊半径。两趟盒式模糊的等效 sigma 约 0.8×半径，
    /// 取 7 时视觉上接近原来 1× 下的 BlurEffect Radius=18（取 9 明显更糊）。</summary>
    private const int BackdropSnapshotBlurRadius = 7;

    private bool _backdropEnabled = true;

    /// <summary>临时开关毛玻璃层（性能对照用）。</summary>
    public void SetBackdropEnabled(bool enabled)
    {
        _backdropEnabled = enabled;
        if (!enabled)
        {
            BackdropSnapshot.Source = null;
            BackdropSnapshot.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>把当前毛玻璃位图存成 PNG（诊断用）。</summary>
    public string SaveBackdrop(string path)
    {
        if (BackdropSnapshot.Source is not BitmapSource source)
        {
            return "no snapshot";
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = System.IO.File.Create(path);
        encoder.Save(stream);
        return path + " " + source.PixelWidth + "x" + source.PixelHeight;
    }

    /// <summary>毛玻璃背景的当前状态（性能/回归诊断用）。</summary>
    public object DescribeBackdrop() => new
    {
        visible = BackdropSnapshot.Visibility.ToString(),
        hasSource = BackdropSnapshot.Source is not null,
        hostWidth = Math.Round(BackdropHost.ActualWidth),
        hostHeight = Math.Round(BackdropHost.ActualHeight),
        cacheWidth = _backdropCacheWidth,
        cacheHeight = _backdropCacheHeight,
        dirty = _backdropDirty,
        captures = _backdropCaptures,
        lastCaptureMs = Math.Round(_lastCaptureMs, 2),
        lastError = _lastBackdropError,
    };

    private string _lastBackdropError = string.Empty;
    private int _backdropCaptures;
    private double _lastCaptureMs;

    /// <summary>弹窗开/关、窗口尺寸变化时刷新毛玻璃背景。</summary>
    private void UpdateBackdrop()
    {
        if (!_vm.IsFolderOpen || !_backdropEnabled)
        {
            RestoreBackdrop();
            return;
        }

        var width = (int)Math.Ceiling(BackdropHost.ActualWidth * BackdropSnapshotScale);
        var height = (int)Math.Ceiling(BackdropHost.ActualHeight * BackdropSnapshotScale);
        if (width < 16 || height < 16)
        {
            RestoreBackdrop();
            return;
        }

        // 背景没变就直接复用上次的模糊位图：反复开关弹窗时几乎没有额外开销
        if (!_backdropDirty && _backdropCache is not null &&
            _backdropCacheWidth == width && _backdropCacheHeight == height)
        {
            ShowBackdrop(_backdropCache);
            return;
        }

        try
        {
            var captureWatch = System.Diagnostics.Stopwatch.StartNew();
            var dpi = 96 * BackdropSnapshotScale;
            var snapshot = new RenderTargetBitmap(width, height, dpi, dpi, PixelFormats.Pbgra32);
            snapshot.Render(BackdropHost);
            snapshot.Freeze();

            // 窗口背景是 DWM Mica，WPF 层本身透明：先铺一层不透明底色，否则截图里只有白字/图标，
            // 合成到遮罩下面等于没截（背景还是原样透出来）。
            var grounded = Ground(snapshot, width, height);
            _backdropCache = Blur(grounded, width, height, BackdropSnapshotBlurRadius);
            _backdropCacheWidth = width;
            _backdropCacheHeight = height;
            _backdropDirty = false;
            _backdropCaptures++;
            _lastCaptureMs = captureWatch.Elapsed.TotalMilliseconds;

            ShowBackdrop(_backdropCache);
        }
        catch (Exception exception)
        {
            _lastBackdropError = exception.GetType().Name + ": " + exception.Message;
            RestoreBackdrop();
        }
    }

    private void ShowBackdrop(BitmapSource source)
    {
        // 快照不透明，直接盖住下层即可；不隐藏 BackdropHost，
        // 避免每次开关都让 WPF 重新光栅化整棵内容树（那才是开关弹窗的 CPU 大头）。
        BackdropSnapshot.Source = source;
        BackdropSnapshot.Visibility = Visibility.Visible;
    }

    private BitmapSource? _backdropCache;
    private int _backdropCacheWidth;
    private int _backdropCacheHeight;
    private bool _backdropDirty = true;
    private bool _backdropRefreshQueued;

    /// <summary>背景内容变了（切左栏/筛选/切视图/重扫/改尺寸）——下次开弹窗要重新截图。</summary>
    private void MarkBackdropDirty() => _backdropDirty = true;

    /// <summary>窗口尺寸变化时合并重截请求（拖动缩放会连发很多次）。</summary>
    private void QueueBackdropRefresh()
    {
        if (!_vm.IsFolderOpen || _backdropRefreshQueued)
        {
            return;
        }

        _backdropRefreshQueued = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _backdropRefreshQueued = false;
            if (_vm.IsFolderOpen)
            {
                UpdateBackdrop();
            }
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void RestoreBackdrop()
    {

        if (BackdropSnapshot.Visibility != Visibility.Collapsed)
        {
            BackdropSnapshot.Visibility = Visibility.Collapsed;
            BackdropSnapshot.Source = null;
        }


    }

    /// <summary>把半透明截图铺到不透明底色上（模拟 Mica 之后的观感）。</summary>
    private BitmapSource Ground(BitmapSource snapshot, int width, int height)
    {
        var background = TryFindResource("BackdropBaseBrush") as Brush ?? Brushes.Black;

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(background, null, new Rect(0, 0, width, height));
            context.DrawImage(snapshot, new Rect(0, 0, width, height));
        }

        var composed = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        composed.Render(visual);
        composed.Freeze();
        return composed;
    }

    /// <summary>两趟可分离盒式模糊（近似高斯）。对预乘 BGRA 逐通道处理，透明像素是纯黑，不会串色。</summary>
    private static BitmapSource Blur(BitmapSource source, int width, int height, int radius)
    {
        var stride = width * 4;
        var buffer = new byte[stride * height];
        source.CopyPixels(buffer, stride, 0);

        var scratch = new byte[buffer.Length];
        radius = Math.Clamp(radius, 1, 24);

        Horizontal(buffer, scratch, width, height, stride, radius);
        Vertical(scratch, buffer, width, height, stride, radius);
        Horizontal(buffer, scratch, width, height, stride, radius);
        Vertical(scratch, buffer, width, height, stride, radius);

        var dpi = 96 * BackdropSnapshotScale;
        var result = BitmapSource.Create(width, height, dpi, dpi, PixelFormats.Pbgra32, null, buffer, stride);
        result.Freeze();
        return result;
    }

    private static void Horizontal(byte[] source, byte[] target, int width, int height, int stride, int radius)
    {
        var window = radius * 2 + 1;
        for (var y = 0; y < height; y++)
        {
            var row = y * stride;
            int sumB = 0, sumG = 0, sumR = 0, sumA = 0;

            for (var i = -radius; i <= radius; i++)
            {
                var offset = row + Math.Clamp(i, 0, width - 1) * 4;
                sumB += source[offset];
                sumG += source[offset + 1];
                sumR += source[offset + 2];
                sumA += source[offset + 3];
            }

            for (var x = 0; x < width; x++)
            {
                var offset = row + x * 4;
                target[offset] = (byte)(sumB / window);
                target[offset + 1] = (byte)(sumG / window);
                target[offset + 2] = (byte)(sumR / window);
                target[offset + 3] = (byte)(sumA / window);

                var added = row + Math.Clamp(x + radius + 1, 0, width - 1) * 4;
                var removed = row + Math.Clamp(x - radius, 0, width - 1) * 4;
                sumB += source[added] - source[removed];
                sumG += source[added + 1] - source[removed + 1];
                sumR += source[added + 2] - source[removed + 2];
                sumA += source[added + 3] - source[removed + 3];
            }
        }
    }

    private static void Vertical(byte[] source, byte[] target, int width, int height, int stride, int radius)
    {
        var window = radius * 2 + 1;
        for (var x = 0; x < width; x++)
        {
            var column = x * 4;
            int sumB = 0, sumG = 0, sumR = 0, sumA = 0;

            for (var i = -radius; i <= radius; i++)
            {
                var offset = Math.Clamp(i, 0, height - 1) * stride + column;
                sumB += source[offset];
                sumG += source[offset + 1];
                sumR += source[offset + 2];
                sumA += source[offset + 3];
            }

            for (var y = 0; y < height; y++)
            {
                var offset = y * stride + column;
                target[offset] = (byte)(sumB / window);
                target[offset + 1] = (byte)(sumG / window);
                target[offset + 2] = (byte)(sumR / window);
                target[offset + 3] = (byte)(sumA / window);

                var added = Math.Clamp(y + radius + 1, 0, height - 1) * stride + column;
                var removed = Math.Clamp(y - radius, 0, height - 1) * stride + column;
                sumB += source[added] - source[removed];
                sumG += source[added + 1] - source[removed + 1];
                sumR += source[added + 2] - source[removed + 2];
                sumA += source[added + 3] - source[removed + 3];
            }
        }
    }
}
