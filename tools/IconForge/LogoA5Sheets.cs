using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Custodian.IconForge;

/// <summary>Review sheets for A5 and the A4b variant (54 degree opening).</summary>
internal static class LogoA5Sheets
{
    private static readonly LogoVariant[] Variants =
    {
        LogoVariant.A5, LogoVariant.A4B, LogoVariant.A2, LogoVariant.Old,
    };

    private static readonly string[] Labels =
    {
        "A5 · 实心盒 + 掀盖", "A4b · 粗几何 C（54°）", "A2 · 描边盒盖片", "旧版（对比）",
    };

    public static int Run(CliOptions options)
    {
        string outDir = options.Get("out") is { Length: > 0 } custom
            ? Path.GetFullPath(custom)
            : Path.Combine(Path.GetTempPath(), "custodian-logo-v2");
        Directory.CreateDirectory(outDir);

        int[] allSizes = { 1024, 512, 256, 128, 64, 48, 32, 24, 20, 16 };
        int[] sheetSizes = { 512, 256, 128, 64, 48, 32, 24, 20, 16 };
        var cache = new Dictionary<(LogoVariant, int), BitmapSource>();

        foreach (LogoVariant variant in Variants)
        {
            foreach (int size in allSizes)
            {
                cache[(variant, size)] = LogoA2Sheets.Render(variant, size);
            }
        }

        foreach (int size in allSizes)
        {
            IconRender.SavePng(cache[(LogoVariant.A5, size)], Path.Combine(outDir, $"a5-{size}.png"));
        }

        foreach (int size in new[] { 256, 32, 16 })
        {
            IconRender.SavePng(cache[(LogoVariant.A4B, size)], Path.Combine(outDir, $"a4b-{size}.png"));
        }

        SaveContact(Path.Combine(outDir, "contact-dark.png"), Color.FromRgb(0x1B, 0x1C, 0x1F), true, cache);
        SaveContact(Path.Combine(outDir, "contact-light.png"), Color.FromRgb(0xF3, 0xF3, 0xF3), false, cache);
        SaveTaskbar(Path.Combine(outDir, "taskbar-dark.png"), Color.FromRgb(0x1E, 0x1E, 0x1E), true, cache);
        SaveTaskbar(Path.Combine(outDir, "taskbar-light.png"), Color.FromRgb(0xF3, 0xF3, 0xF3), false, cache);
        SaveZoom(Path.Combine(outDir, "zoom-16-8x.png"), 16, 8, cache);
        SaveZoom(Path.Combine(outDir, "zoom-24-6x.png"), 24, 6, cache);

        Console.WriteLine($"[logo-a5] 输出目录: {outDir}");
        Console.WriteLine();
        Console.WriteLine("[logo-a5] 重心 / 覆盖率:");
        foreach (LogoVariant variant in Variants)
        {
            foreach (int size in new[] { 256, 32, 16 })
            {
                (double dx, double dy, double coverage) = MeasureCentroid(cache[(variant, size)]);
                Console.WriteLine($"  {Tag(variant),-4} {size,3}px  dx={dx,6:+0.00;-0.00;-0.00}%  dy={dy,6:+0.00;-0.00;-0.00}%  白覆盖率={coverage,5:F1}%");
            }
        }

        Console.WriteLine();
        foreach (string file in Directory.GetFiles(outDir, "a5-*.png").OrderBy(f => f, StringComparer.Ordinal))
        {
            Console.WriteLine($"  {Path.GetFileName(file),-16} {new FileInfo(file).Length,9:N0} B");
        }

        _ = sheetSizes;
        return 0;
    }

    private static string Tag(LogoVariant variant) => variant switch
    {
        LogoVariant.A2 => "a2",
        LogoVariant.A3 => "a3",
        LogoVariant.A4 => "a4",
        LogoVariant.A4B => "a4b",
        LogoVariant.A5 => "a5",
        _ => "old",
    };

    private static void SaveContact(
        string path, Color background, bool dark, IReadOnlyDictionary<(LogoVariant, int), BitmapSource> cache)
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
                LogoVariant variant = Variants[i];
                double x = padding + columnWidth * i;
                double centerX = x + columnWidth / 2.0;

                dc.DrawImage(cache[(variant, 256)], new Rect(centerX - 128, heroY, 256, 256));

                double rowX = centerX - (64 + 12 + 32 + 12 + 16) / 2.0;
                dc.DrawImage(cache[(variant, 64)], new Rect(rowX, rowY, 64, 64));
                rowX += 76;
                dc.DrawImage(cache[(variant, 32)], new Rect(rowX, rowY + 16, 32, 32));
                rowX += 44;
                dc.DrawImage(cache[(variant, 16)], new Rect(rowX, rowY + 24, 16, 16));

                FormattedText title = Text(Labels[i], 15, labelBrush, FontWeights.SemiBold);
                dc.DrawText(title, new Point(centerX - title.Width / 2.0, labelY));

                FormattedText hint = Text("1024 设计坐标 · 256 / 64 / 32 / 16", 11, hintBrush, FontWeights.Normal);
                dc.DrawText(hint, new Point(centerX - hint.Width / 2.0, labelY + 24));
            }
        }

        Save(visual, path, width, height);
    }

    private static void SaveTaskbar(
        string path, Color background, bool dark, IReadOnlyDictionary<(LogoVariant, int), BitmapSource> cache)
    {
        const double labelWidth = 46;
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
                LogoVariant variant = Variants[i];
                double top = 6 + rowHeight * i;

                FormattedText label = Text(Tag(variant).ToUpperInvariant(), 12, labelBrush, FontWeights.SemiBold);
                dc.DrawText(label, new Point(14, top + rowHeight / 2.0 - label.Height / 2.0));

                foreach ((int size, double x) in row)
                {
                    double y = top + (rowHeight - size) / 2.0;
                    dc.DrawImage(cache[(variant, size)], new Rect(labelWidth + x, y, size, size));
                }
            }
        }

        Save(visual, path, width, height);
    }

    private static void SaveZoom(
        string path, int size, int factor, IReadOnlyDictionary<(LogoVariant, int), BitmapSource> cache)
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
                double x = padding + (cell + 16) * i;
                dc.DrawImage(cache[(Variants[i], size)], new Rect(x, padding, cell, cell));

                FormattedText text = Text($"{Tag(Variants[i]).ToUpperInvariant()} · {size}px ×{factor}", 12, labelBrush, FontWeights.Normal);
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

        double dx = (sumX / sum - width / 2.0) / width * 100.0;
        double dy = (sumY / sum - height / 2.0) / height * 100.0;
        return (dx, dy, bright * 100.0 / (width * height));
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
