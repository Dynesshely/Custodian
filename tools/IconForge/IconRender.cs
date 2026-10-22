using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Custodian.IconForge;

/// <summary>Rasterises <see cref="IconDesign"/> at any pixel size and encodes PNG / ICO payloads.</summary>
internal static class IconRender
{
    /// <summary>Renders the mark at <paramref name="size"/> and returns straight-alpha BGRA32 pixels.</summary>
    public static BitmapSource Render(int size, int supersample = -1) =>
        RenderCustom((dc, designSize) => LogoFinal.Draw(dc, designSize), size, supersample);

    /// <summary>Renders any 1024-unit vector drawing with the standard supersampling policy.</summary>
    public static BitmapSource RenderCustom(Action<DrawingContext, int> draw, int size, int supersample = -1)
    {
        int factor = supersample >= 0 ? supersample : SupersampleFactor(size);
        if (factor <= 1)
        {
            return ToBgra32(RenderRaw(size, size, draw));
        }

        BitmapSource highResolution = RenderRaw(size * factor, size, draw);
        return ToBgra32(Downscale(highResolution, size));
    }

    /// <summary>
    /// Small sizes are rendered at 4x and filtered down; the geometry has thin
    /// strokes at 16 px, so supersampling keeps the ring white instead of grey.
    /// </summary>
    public static int SupersampleFactor(int size) => size switch
    {
        16 => 1,      // geometry is pixel aligned at 16 px, direct AA is sharpest
        20 or 24 => 4,
        32 or 40 => 2,
        _ => 1,
    };

    public static bool IsPngEntry(int size) => size >= 128;

    public static byte[] EncodePng(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));

        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    public static void SavePng(BitmapSource image, string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(path, EncodePng(image));
    }

    /// <summary>Nearest-neighbour upscale, used by the preview sheets for pixel inspection.</summary>
    public static BitmapSource ZoomNearest(BitmapSource source, int factor)
    {
        int size = source.PixelWidth * factor;
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawImage(source, new Rect(0, 0, size, size));
        }

        var target = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }

    /// <summary>Converts a premultiplied WPF bitmap into straight-alpha BGRA32.</summary>
    public static BitmapSource ToBgra32(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap();
        converted.BeginInit();
        converted.Source = source;
        converted.DestinationFormat = PixelFormats.Bgra32;
        converted.EndInit();
        converted.Freeze();

        if (converted.Format == PixelFormats.Bgra32 && converted.CanFreeze)
        {
            return converted;
        }

        var copy = new WriteableBitmap(converted);
        copy.Freeze();
        return copy;
    }

    private static RenderTargetBitmap RenderRaw(int pixelSize, int designPixelSize, Action<DrawingContext, int> draw)
    {
        var visual = new DrawingVisual();
        RenderOptions.SetEdgeMode(visual, EdgeMode.Unspecified);

        using (DrawingContext dc = visual.RenderOpen())
        {
            double scale = pixelSize / IconDesign.DesignSize;
            dc.PushTransform(new ScaleTransform(scale, scale));
            draw(dc, designPixelSize);
            dc.Pop();
        }

        var bitmap = new RenderTargetBitmap(pixelSize, pixelSize, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static BitmapSource Downscale(BitmapSource source, int size)
    {
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawImage(source, new Rect(0, 0, size, size));
        }

        var target = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }
}