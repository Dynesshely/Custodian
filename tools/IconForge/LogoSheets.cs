using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Custodian.IconForge;

/// <summary>
/// Renders the v2 logo candidates and the review sheets (contact sheet + taskbar
/// strips) into a scratch folder. Never touches assets/ - this is review only.
/// </summary>
internal static class LogoSheets
{
    private static readonly LogoConcept[] Candidates =
    {
        LogoConcept.A, LogoConcept.B, LogoConcept.C, LogoConcept.Old,
    };

    private static readonly string[] Labels =
    {
        "A · 方盒（盖口）", "B · 开口环 C", "C · 三层叠片", "旧版（对比）",
    };

    private static readonly int[] DetailSizes = { 1024, 256, 64, 32, 16 };

    public static int Run(CliOptions options)
    {
        string outDir = options.Get("out") is { Length: > 0 } custom
            ? Path.GetFullPath(custom)
            : Path.Combine(Path.GetTempPath(), "custodian-logo-v2");
        Directory.CreateDirectory(outDir);

        var cache = new Dictionary<(LogoConcept, int), BitmapSource>();

        foreach (LogoConcept concept in Candidates)
        {
            foreach (int size in DetailSizes)
            {
                BitmapSource image = RenderConcept(concept, size);
                cache[(concept, size)] = image;
                IconRender.SavePng(image, Path.Combine(outDir, $"{Slug(concept)}-{size}.png"));
            }

            // 20 / 24 px are only needed for the taskbar strips.
            cache[(concept, 20)] = RenderConcept(concept, 20);
            cache[(concept, 24)] = RenderConcept(concept, 24);
        }

        SaveSheet(Path.Combine(outDir, "contact-dark.png"), Color.FromRgb(0x1B, 0x1C, 0x1F), true, cache);
        SaveSheet(Path.Combine(outDir, "contact-light.png"), Color.FromRgb(0xF3, 0xF3, 0xF3), false, cache);
        SaveTaskbar(Path.Combine(outDir, "taskbar-dark.png"), Color.FromRgb(0x1E, 0x1E, 0x1E), true, cache);
        SaveTaskbar(Path.Combine(outDir, "taskbar-light.png"), Color.FromRgb(0xF3, 0xF3, 0xF3), false, cache);
        SaveZoomSheet(Path.Combine(outDir, "zoom-16-8x.png"), 16, 8, cache);
        SaveZoomSheet(Path.Combine(outDir, "zoom-32-4x.png"), 32, 4, cache);

        Console.WriteLine($"[logo-v2] 输出目录: {outDir}");
        foreach (string file in Directory.GetFiles(outDir, "*.png").OrderBy(f => f, StringComparer.Ordinal))
        {
            Console.WriteLine($"  {Path.GetFileName(file),-20} {new FileInfo(file).Length,9:N0} B");
        }

        return 0;
    }

    private static BitmapSource RenderConcept(LogoConcept concept, int size) =>
        IconRender.RenderCustom((dc, designSize) => LogoV2.Draw(dc, concept, designSize), size);

    private static string Slug(LogoConcept concept) => concept switch
    {
        LogoConcept.A => "a",
        LogoConcept.B => "b",
        LogoConcept.C => "c",
        _ => "old",
    };

    /// <summary>Four up comparison: 256 hero plus a 64 / 32 / 16 row per candidate.</summary>
    private static void SaveSheet(
        string path, Color background, bool dark, IReadOnlyDictionary<(LogoConcept, int), BitmapSource> cache)
    {
        const double padding = 24;
        const double columnWidth = 300;
        const double heroY = 34;
        const double rowY = 330;
        const double labelY = 410;

        double width = padding * 2 + columnWidth * Candidates.Length;
        double height = 460;

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

            for (int i = 0; i < Candidates.Length; i++)
            {
                LogoConcept concept = Candidates[i];
                double x = padding + columnWidth * i;
                double centerX = x + columnWidth / 2.0;

                BitmapSource hero = cache[(concept, 256)];
                dc.DrawImage(hero, new Rect(centerX - 128, heroY, 256, 256));

                double rowX = centerX - (64 + 12 + 32 + 12 + 16) / 2.0;
                dc.DrawImage(cache[(concept, 64)], new Rect(rowX, rowY, 64, 64));
                rowX += 76;
                dc.DrawImage(cache[(concept, 32)], new Rect(rowX, rowY + 16, 32, 32));
                rowX += 44;
                dc.DrawImage(cache[(concept, 16)], new Rect(rowX, rowY + 24, 16, 16));

                FormattedText title = Text(Labels[i], 15, labelBrush, FontWeights.SemiBold);
                dc.DrawText(title, new Point(centerX - title.Width / 2.0, labelY));

                FormattedText hint = Text("1024 设计坐标 · 256 / 64 / 32 / 16", 11, hintBrush, FontWeights.Normal);
                dc.DrawText(hint, new Point(centerX - hint.Width / 2.0, labelY + 24));
            }
        }

        Save(visual, path, width, height);
    }

    /// <summary>Taskbar simulation: each candidate as 32 / 24 / 20 / 16 px on a bar.</summary>
    private static void SaveTaskbar(
        string path, Color background, bool dark, IReadOnlyDictionary<(LogoConcept, int), BitmapSource> cache)
    {
        const double labelWidth = 46;
        const double rowHeight = 52;
        (int Size, double X)[] row =
        {
            (32, 0), (24, 52), (20, 94), (16, 128),
        };

        double width = labelWidth + 170;
        double height = rowHeight * Candidates.Length + 12;

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(background), null, new Rect(0, 0, width, height));
            Brush labelBrush = new SolidColorBrush(dark
                ? Color.FromRgb(0xC8, 0xC8, 0xCC)
                : Color.FromRgb(0x44, 0x44, 0x48));

            for (int i = 0; i < Candidates.Length; i++)
            {
                LogoConcept concept = Candidates[i];
                double top = 6 + rowHeight * i;

                FormattedText label = Text(concept.ToString(), 12, labelBrush, FontWeights.SemiBold);
                dc.DrawText(label, new Point(14, top + rowHeight / 2.0 - label.Height / 2.0));

                foreach ((int size, double x) in row)
                {
                    double y = top + (rowHeight - size) / 2.0;
                    dc.DrawImage(cache[(concept, size)], new Rect(labelWidth + x, y, size, size));
                }
            }
        }

        Save(visual, path, width, height);
    }

    /// <summary>Nearest-neighbour zoom of the same size for every candidate.</summary>
    private static void SaveZoomSheet(
        string path, int size, int factor, IReadOnlyDictionary<(LogoConcept, int), BitmapSource> cache)
    {
        double cell = size * factor;
        const double padding = 20;
        double width = padding * 2 + cell * Candidates.Length + 16 * (Candidates.Length - 1);
        double height = cell + 52;

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x1B, 0x1C, 0x1F)), null, new Rect(0, 0, width, height));
            Brush labelBrush = new SolidColorBrush(Color.FromRgb(0xD8, 0xD8, 0xDC));

            for (int i = 0; i < Candidates.Length; i++)
            {
                double x = padding + (cell + 16) * i;
                dc.DrawImage(cache[(Candidates[i], size)], new Rect(x, padding, cell, cell));

                string caption = $"{Candidates[i]} · {size}px ×{factor}";
                FormattedText text = Text(caption, 12, labelBrush, FontWeights.Normal);
                dc.DrawText(text, new Point(x, padding + cell + 8));
            }
        }

        Save(visual, path, width, height);
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