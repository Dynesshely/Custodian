using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Custodian.IconForge;

/// <summary>Review sheets for A4c on both plate palettes (graphite / ink blue).</summary>
internal static class LogoA4cSheets
{
    private static readonly (string Key, string Label)[] Variants =
    {
        ("a4c-graphite", "A4c · 石墨底板"),
        ("a4c-inkblue", "A4c · 墨蓝底板"),
        ("a4b", "A4b · 环形居中方块"),
        ("old", "旧版（对比）"),
    };

    public static int Run(CliOptions options)
    {
        string outDir = options.Get("out") is { Length: > 0 } custom
            ? Path.GetFullPath(custom)
            : Path.Combine(Path.GetTempPath(), "custodian-logo-v2");
        Directory.CreateDirectory(outDir);

        int[] sizes = { 1024, 512, 256, 128, 64, 48, 32, 24, 20, 16 };
        int[] saved = { 1024, 256, 64, 32, 16 };
        var cache = new Dictionary<(string Key, int Size), BitmapSource>();

        foreach ((string key, _) in Variants)
        {
            foreach (int size in sizes)
            {
                cache[(key, size)] = Render(key, size);
            }
        }

        foreach (string key in new[] { "a4c-graphite", "a4c-inkblue" })
        {
            foreach (int size in saved)
            {
                IconRender.SavePng(cache[(key, size)], Path.Combine(outDir, $"{key}-{size}.png"));
            }
        }

        SaveContact(Path.Combine(outDir, "contact-dark.png"), Color.FromRgb(0x1B, 0x1C, 0x1F), true, cache);
        SaveContact(Path.Combine(outDir, "contact-light.png"), Color.FromRgb(0xF3, 0xF3, 0xF3), false, cache);
        SaveTaskbar(Path.Combine(outDir, "taskbar-dark.png"), Color.FromRgb(0x1E, 0x1E, 0x1E), true, cache);
        SaveTaskbar(Path.Combine(outDir, "taskbar-light.png"), Color.FromRgb(0xE9, 0xE9, 0xEC), false, cache);
        SaveZoom(Path.Combine(outDir, "zoom-16-8x.png"), 16, 8, cache);

        Console.WriteLine($"[logo-a4c] 输出目录: {outDir}");
        Console.WriteLine();
        foreach ((string key, _) in Variants)
        {
            foreach (int size in new[] { 256, 32, 16 })
            {
                (double dx, double dy, double coverage) = MeasureCentroid(cache[(key, size)]);
                Console.WriteLine($"  {key,-13} {size,3}px  dx={dx,6:+0.00;-0.00;-0.00}%  dy={dy,6:+0.00;-0.00;-0.00}%  白覆盖率={coverage,5:F1}%");
            }
        }

        return 0;
    }

    public static BitmapSource RenderVariant(int size, double gap, double core, double offset) =>
        IconRender.RenderCustom((dc, s) => LogoA4c.Draw(dc, s, PlatePalette.Graphite, gap, core, offset), size);

    private static BitmapSource Render(string key, int size) => key switch
    {
        "a4c-graphite" => IconRender.RenderCustom((dc, s) => LogoA4c.Draw(dc, s, PlatePalette.Graphite), size),
        "a4c-inkblue" => IconRender.RenderCustom((dc, s) => LogoA4c.Draw(dc, s, PlatePalette.InkBlue), size),
        "a4b" => IconRender.RenderCustom((dc, s) => LogoA4.Draw(dc, s, 54.0), size),
        _ => IconRender.RenderCustom((dc, s) => IconDesign.Draw(dc, s), size),
    };

    private static void SaveContact(
        string path, Color background, bool dark, IReadOnlyDictionary<(string Key, int Size), BitmapSource> cache)
    {
        const double padding = 24;
        const double columnWidth = 300;
        const double heroY = 34;
        const double rowY = 330;
        const double labelY = 410;

        double width = padding * 2 + columnWidth * Variants.Length;
        const double height = 460;

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
                double x = padding + columnWidth * i;
                double centerX = x + columnWidth / 2.0;

                dc.DrawImage(cache[(key, 256)], new Rect(centerX - 128, heroY, 256, 256));

                double rowX = centerX - (64 + 12 + 32 + 12 + 16) / 2.0;
                dc.DrawImage(cache[(key, 64)], new Rect(rowX, rowY, 64, 64));
                rowX += 76;
                dc.DrawImage(cache[(key, 32)], new Rect(rowX, rowY + 16, 32, 32));
                rowX += 44;
                dc.DrawImage(cache[(key, 16)], new Rect(rowX, rowY + 24, 16, 16));

                FormattedText title = Text(label, 15, labelBrush, FontWeights.SemiBold);
                dc.DrawText(title, new Point(centerX - title.Width / 2.0, labelY));

                FormattedText hint = Text("1024 设计坐标 · 256 / 64 / 32 / 16", 11, hintBrush, FontWeights.Normal);
                dc.DrawText(hint, new Point(centerX - hint.Width / 2.0, labelY + 24));
            }
        }

        Save(visual, path, width, height);
    }

    private static void SaveTaskbar(
        string path, Color background, bool dark, IReadOnlyDictionary<(string Key, int Size), BitmapSource> cache)
    {
        const double labelWidth = 76;
        const double rowHeight = 52;
        (int Size, double X)[] row = { (32, 0), (24, 52), (20, 94), (16, 128) };

        double width = labelWidth + 170;
        double height = rowHeight * Variants.Length + 12;

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(background), null, new Rect(0, 0, width, height));
            Brush labelBrush = new SolidColorBrush(dark
                ? Color.FromRgb(0xC8, 0xC8, 0xCC)
                : Color.FromRgb(0x44, 0x44, 0x48));

            for (int i = 0; i < Variants.Length; i++)
            {
                (string key, _) = Variants[i];
                double top = 6 + rowHeight * i;
                string name = key switch
                {
                    "a4c-graphite" => "A4c 石墨",
                    "a4c-inkblue" => "A4c 墨蓝",
                    "a4b" => "A4b",
                    _ => "OLD",
                };

                FormattedText label = Text(name, 12, labelBrush, FontWeights.SemiBold);
                dc.DrawText(label, new Point(12, top + rowHeight / 2.0 - label.Height / 2.0));

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
        double cell = size * factor;
        const double padding = 20;
        double width = padding * 2 + cell * Variants.Length + 16 * (Variants.Length - 1);
        double height = cell + 52;

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x1B, 0x1C, 0x1F)), null, new Rect(0, 0, width, height));
            Brush labelBrush = new SolidColorBrush(Color.FromRgb(0xD8, 0xD8, 0xDC));

            for (int i = 0; i < Variants.Length; i++)
            {
                (string key, _) = Variants[i];
                double x = padding + (cell + 16) * i;
                dc.DrawImage(cache[(key, size)], new Rect(x, padding, cell, cell));

                FormattedText text = Text($"{key} · {size}px ×{factor}", 11, labelBrush, FontWeights.Normal);
                dc.DrawText(text, new Point(x, padding + cell + 8));
            }
        }

        Save(visual, path, width, height);
    }

    private static (double Dx, double Dy, double Coverage) MeasureCentroid(BitmapSource image)
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
                double weight = Math.Max(0.0, luma - 140.0);
                if (weight <= 0)
                {
                    continue;
                }

                bright++;
                sum += weight;
                sumX += weight * (x + 0.5);
                sumY += weight * (y + 0.5);
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