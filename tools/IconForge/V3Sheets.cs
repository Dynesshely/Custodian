using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Custodian.IconForge;

/// <summary>Review sheets for the v3 sandbox-family candidates.</summary>
internal static class V3Sheets
{
    private static readonly (string Key, string Label)[] Variants =
    {
        ("p1", "P1 · 浅底蓝盒"),
        ("p2", "P2 · 蓝底白盒"),
        ("p3", "P3 · 深浅蓝底"),
        ("sandman", "SandMan-32 参照 ×8"),
        ("a4b", "现版 A4b · 墨蓝"),
    };

    public static int Run(CliOptions options)
    {
        string outDir = options.Get("out") is { Length: > 0 } custom
            ? Path.GetFullPath(custom)
            : Path.Combine(Path.GetTempPath(), "custodian-logo-v2");
        Directory.CreateDirectory(outDir);

        int[] sizes = { 1024, 512, 256, 128, 64, 48, 32, 24, 20, 16 };
        int[] saved = { 256, 64, 32, 16 };
        var cache = new Dictionary<(string Key, int Size), BitmapSource>();

        foreach (string key in new[] { "p1", "p2", "p3", "a4b" })
        {
            foreach (int size in sizes)
            {
                cache[(key, size)] = Render(key, size);
            }
        }

        foreach (string key in new[] { "p1", "p2", "p3" })
        {
            foreach (int size in saved)
            {
                IconRender.SavePng(cache[(key, size)], Path.Combine(outDir, $"v3-{key}-{size}.png"));
            }
        }

        cache[("sandman", 32)] = LoadReference("SandMan-32.png");
        cache[("sandman", 64)] = LoadReference("SandMan-64.png");

        SaveContact(Path.Combine(outDir, "v3-contact-dark.png"), Color.FromRgb(0x1B, 0x1C, 0x1F), true, cache);
        SaveContact(Path.Combine(outDir, "v3-contact-light.png"), Color.FromRgb(0xF3, 0xF3, 0xF3), false, cache);
        SaveTaskbar(Path.Combine(outDir, "v3-taskbar-dark.png"), Color.FromRgb(0x1E, 0x1E, 0x1E), true, cache);
        SaveTaskbar(Path.Combine(outDir, "v3-taskbar-light.png"), Color.FromRgb(0xE9, 0xE9, 0xEC), false, cache);
        SaveZoom(Path.Combine(outDir, "v3-zoom-16-8x.png"), 16, 8, cache);
        SaveZoom(Path.Combine(outDir, "v3-zoom-32-4x.png"), 32, 4, cache);

        Console.WriteLine($"[v3] 输出目录: {outDir}");
        Console.WriteLine();
        foreach (string key in new[] { "p1", "p2", "p3" })
        {
            foreach (int size in new[] { 256, 32, 16 })
            {
                (double dx, double dy, double coverage) = Measure(cache[(key, size)]);
                Console.WriteLine($"  {key} {size,3}px  dx={dx,6:+0.00;-0.00;-0.00}%  dy={dy,6:+0.00;-0.00;-0.00}%  覆盖={coverage,5:F1}%");
            }
        }

        return 0;
    }

    private static BitmapSource Render(string key, int size) => key switch
    {
        "p1" => IconRender.RenderCustom((dc, s) => LogoV3.Draw(dc, s, V3Palette.P1), size),
        "p2" => IconRender.RenderCustom((dc, s) => LogoV3.Draw(dc, s, V3Palette.P2), size),
        "p3" => IconRender.RenderCustom((dc, s) => LogoV3.Draw(dc, s, V3Palette.P3), size),
        _ => IconRender.RenderCustom((dc, s) => LogoA4.Draw(dc, s, 54.0, true), size),
    };

    private static BitmapSource LoadReference(string fileName)
    {
        string path = Path.Combine(Path.GetTempPath(), "sbicons", fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"缺少参考图: {path}");
        }

        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(path);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static void SaveContact(
        string path, Color background, bool dark, IReadOnlyDictionary<(string Key, int Size), BitmapSource> cache)
    {
        const double padding = 20;
        const double column = 250;
        double width = padding * 2 + column * Variants.Length;
        const double height = 420;

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(background), null, new Rect(0, 0, width, height));
            Brush labelBrush = new SolidColorBrush(dark
                ? Color.FromRgb(0xE8, 0xE8, 0xE8)
                : Color.FromRgb(0x2A, 0x2A, 0x2A));
            Brush hintBrush = new SolidColorBrush(dark
                ? Color.FromRgb(0x8A, 0x8A, 0x8F)
                : Color.FromRgb(0x70, 0x70, 0x74));

            for (int i = 0; i < Variants.Length; i++)
            {
                (string key, string label) = Variants[i];
                double centerX = padding + column * i + column / 2.0;

                if (key == "sandman")
                {
                    // 32 px reference at 8x so the simplification stays visible.
                    RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
                    dc.DrawImage(cache[("sandman", 32)], new Rect(centerX - 128, 22, 256, 256));
                    RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
                }
                else
                {
                    dc.DrawImage(cache[(key, 256)], new Rect(centerX - 128, 22, 256, 256));
                }

                double rowX = centerX - (64 + 12 + 32 + 12 + 16) / 2.0;
                if (cache.ContainsKey((key, 64)))
                {
                    dc.DrawImage(cache[(key, 64)], new Rect(rowX, 292, 64, 64));
                }

                rowX += 76;
                if (cache.ContainsKey((key, 32)))
                {
                    dc.DrawImage(cache[(key, 32)], new Rect(rowX, 308, 32, 32));
                }

                rowX += 44;
                if (cache.ContainsKey((key, 16)))
                {
                    dc.DrawImage(cache[(key, 16)], new Rect(rowX, 316, 16, 16));
                }

                FormattedText title = Text(label, 14, labelBrush, FontWeights.SemiBold);
                dc.DrawText(title, new Point(centerX - title.Width / 2.0, 372));
            }
        }

        Save(visual, path, width, height);
    }

    private static void SaveTaskbar(
        string path, Color background, bool dark, IReadOnlyDictionary<(string Key, int Size), BitmapSource> cache)
    {
        string[] rows = { "p1", "p2", "p3", "a4b" };
        (int Size, double X)[] row = { (32, 0), (24, 52), (20, 94), (16, 128) };
        const double labelWidth = 54;
        const double rowHeight = 52;
        double width = labelWidth + 176;
        double height = rowHeight * rows.Length + 16;

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(background), null, new Rect(0, 0, width, height));
            Brush labelBrush = new SolidColorBrush(dark
                ? Color.FromRgb(0xC8, 0xC8, 0xCC)
                : Color.FromRgb(0x44, 0x44, 0x48));

            for (int i = 0; i < rows.Length; i++)
            {
                string key = rows[i];
                double top = 8 + rowHeight * i;
                FormattedText text = Text(key.ToUpperInvariant(), 12, labelBrush, FontWeights.SemiBold);
                dc.DrawText(text, new Point(12, top + rowHeight / 2.0 - text.Height / 2.0));

                foreach ((int size, double x) in row)
                {
                    double y = top + (rowHeight - size) / 2.0;
                    dc.DrawImage(cache[(key, size)], new Rect(labelWidth + x, y, size, size));
                }
            }
        }

        Save(visual, path, width, height);
    }

    private static void SaveZoom(
        string path, int size, int factor, IReadOnlyDictionary<(string Key, int Size), BitmapSource> cache)
    {
        string[] keys = { "p1", "p2", "p3", "a4b" };
        double cell = size * factor;
        const double padding = 18;
        double width = padding * 2 + cell * keys.Length + 16 * (keys.Length - 1);
        double height = cell + 46;

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x1B, 0x1C, 0x1F)), null, new Rect(0, 0, width, height));
            Brush labelBrush = new SolidColorBrush(Color.FromRgb(0xD8, 0xD8, 0xDC));

            for (int i = 0; i < keys.Length; i++)
            {
                double x = padding + (cell + 16) * i;
                dc.DrawImage(cache[(keys[i], size)], new Rect(x, padding, cell, cell));
                FormattedText text = Text($"{keys[i].ToUpperInvariant()} {size}px", 12, labelBrush, FontWeights.Normal);
                dc.DrawText(text, new Point(x, padding + cell + 6));
            }
        }

        Save(visual, path, width, height);
    }

    private static (double Dx, double Dy, double Coverage) Measure(BitmapSource image)
    {
        int width = image.PixelWidth;
        int height = image.PixelHeight;
        var pixels = new byte[width * height * 4];
        image.CopyPixels(pixels, width * 4, 0);

        double sum = 0, sumX = 0, sumY = 0;
        int bright = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int offset = (y * width + x) * 4;
                double luma = (pixels[offset] + pixels[offset + 1] + pixels[offset + 2]) / 3.0;
                if (luma <= 190)
                {
                    continue;
                }

                bright++;
                sum += 1;
                sumX += x + 0.5;
                sumY += y + 0.5;
            }
        }

        if (sum <= 0)
        {
            return (0, 0, 0);
        }

        return (
            (sumX / sum - width / 2.0) / width * 100.0,
            (sumY / sum - height / 2.0) / height * 100.0,
            bright * 100.0 / (width * height));
    }

    private static void Save(DrawingVisual visual, string path, double width, double height)
    {
        var target = new RenderTargetBitmap(
            (int)Math.Ceiling(width), (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        IconRender.SavePng(target, path);
    }

    private static FormattedText Text(string value, double emSize, Brush brush, FontWeight weight) =>
        new(value,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal),
            emSize,
            brush,
            1.0);
}
