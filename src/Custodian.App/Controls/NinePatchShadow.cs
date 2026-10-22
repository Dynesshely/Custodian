using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Custodian.App.Controls;

/// <summary>
/// 九宫格软阴影。
///
/// 为什么不用 <see cref="System.Windows.Media.Effects.DropShadowEffect"/>：WPF 的 Effect 会把目标元素
/// 整块离屏光栅化到渲染目标（D3D 表面）再跑模糊 —— 弹窗每开一次就申请一次，反复开关会让私有内存
/// 抬升一截并停在那个平台。这里改成「一张预先算好的圆角模糊位图 + 九宫格拉伸」：
/// 位图只有 96×96（约 36 KB），九个角/边/中心切片复用同一份像素，没有任何离屏渲染。
/// </summary>
public sealed class NinePatchShadow : FrameworkElement
{
    /// <summary>切片宽度（也是位图边长的 1/3），单位 DIP。</summary>
    public const int Slice = 32;

    private static BitmapSource? _source;
    private static CroppedBitmap[]? _tiles;

    /// <summary>烘焙好的阴影位图（黑色 + alpha 渐变，不参与布局）。</summary>
    public static BitmapSource ShadowSource => _source ??= BuildSource();

    protected override void OnRender(DrawingContext drawingContext)
    {
        var tiles = GetTiles();
        if (tiles is null)
        {
            return;
        }

        var width = ActualWidth;
        var height = ActualHeight;
        var slice = (double)Slice;

        if (width <= 2 * slice || height <= 2 * slice)
        {
            return;
        }

        // 四角：原尺寸
        Draw(tiles[0], 0, 0, slice, slice);
        Draw(tiles[2], width - slice, 0, slice, slice);
        Draw(tiles[6], 0, height - slice, slice, slice);
        Draw(tiles[8], width - slice, height - slice, slice, slice);

        // 上下边：横向拉伸
        Draw(tiles[1], slice, 0, width - 2 * slice, slice);
        Draw(tiles[7], slice, height - slice, width - 2 * slice, slice);

        // 左右边：纵向拉伸
        Draw(tiles[3], 0, slice, slice, height - 2 * slice);
        Draw(tiles[5], width - slice, slice, slice, height - 2 * slice);

        // 中心：双向拉伸（被面板盖住，看不见）
        Draw(tiles[4], slice, slice, width - 2 * slice, height - 2 * slice);

        void Draw(ImageSource image, double x, double y, double w, double h)
        {
            if (w > 0 && h > 0)
            {
                drawingContext.DrawImage(image, new Rect(x, y, w, h));
            }
        }
    }

    private static CroppedBitmap[]? GetTiles()
    {
        if (_tiles is not null)
        {
            return _tiles;
        }

        var source = ShadowSource;
        if (source is null)
        {
            return null;
        }

        var tiles = new CroppedBitmap[9];
        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                tiles[row * 3 + column] = new CroppedBitmap(
                    source,
                    new Int32Rect(column * Slice, row * Slice, Slice, Slice));
            }
        }

        return _tiles = tiles;
    }

    /// <summary>烘焙 96×96 的圆角矩形阴影：中心 32×32 是面板本体，向外 32 DIP 内平滑衰减。</summary>
    private static BitmapSource BuildSource(int size = 3 * Slice, double cornerRadius = 14)
    {
        const double maxAlpha = 0.34;
        const double blurDistance = 26;

        var stride = size * 4;
        var pixels = new byte[stride * size];

        // 面板矩形（中心 32×32）向内收缩 0.5，避免边缘被阴影糊住
        var left = Slice;
        var top = Slice;
        var right = 2 * Slice;
        var bottom = 2 * Slice;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var distance = RoundedRectDistance(x + 0.5, y + 0.5, left, top, right, bottom, cornerRadius);
                double coverage;
                if (distance <= 0)
                {
                    coverage = 1;
                }
                else if (distance >= blurDistance)
                {
                    coverage = 0;
                }
                else
                {
                    var t = 1 - distance / blurDistance;
                    coverage = t * t * (3 - 2 * t);
                }

                var alpha = (byte)Math.Round(Math.Clamp(coverage * maxAlpha, 0, 1) * 255);
                var offset = y * stride + x * 4;
                // premultiplied BGRA，纯黑：B/G/R 都是 0
                pixels[offset + 3] = alpha;
            }
        }

        var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>点到圆角矩形边界的近似有符号距离（内部为负）。</summary>
    private static double RoundedRectDistance(
        double x, double y, double left, double top, double right, double bottom, double radius)
    {
        var centerX = (left + right) / 2;
        var centerY = (top + bottom) / 2;
        var halfWidth = (right - left) / 2;
        var halfHeight = (bottom - top) / 2;

        var dx = Math.Abs(x - centerX) - (halfWidth - radius);
        var dy = Math.Abs(y - centerY) - (halfHeight - radius);
        var outsideX = Math.Max(dx, 0);
        var outsideY = Math.Max(dy, 0);
        return Math.Sqrt(outsideX * outsideX + outsideY * outsideY) + Math.Min(Math.Max(dx, dy), 0) - radius;
    }
}
