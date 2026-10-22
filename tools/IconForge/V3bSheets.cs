using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Custodian.IconForge;

/// <summary>Review sheets for v3b (P2 final geometry).</summary>
internal static class V3bSheets
{
    private static readonly (string Key, string Label)[] Columns =
    {
        ("v3d", "v3d · 沙盒家族（无小片）"),
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
        int[] saved = { 256, 128, 64, 48, 32, 24, 20, 16 };
        var cache = new Dictionary<(string Key, int Size), BitmapSource>();

        foreach (int size in sizes)
        {
            cache[("v3d", size)] = IconRender.RenderCustom((dc, s) => LogoV3c.Draw(dc, s), size);
            cache[("a4b", size)] = IconRender.RenderCustom((dc, s) => LogoA4.Draw(dc, s, 54.0, true), size);
        }

        foreach (int size in saved)
        {
            IconRender.SavePng(cache[("v3d", size)], Path.Combine(outDir, $"v3d-{size}.png"));
        }

        cache[("sandman", 32)] = LoadReference("SandMan-32.png");
        cache[("sandman", 64)] = LoadReference("SandMan-64.png");

        SaveContact(Path.Combine(outDir, "v3d-contact-dark.png"), Color.FromRgb(0x1B, 0x1C, 0x1F), true, cache);
        SaveContact(Path.Combine(outDir, "v3d-contact-light.png"), Color.FromRgb(0xF3, 0xF3, 0xF3), false, cache);
        SaveTaskbar(Path.Combine(outDir, "v3d-taskbar-dark.png"), Color.FromRgb(0x1E, 0x1E, 0x1E), true, cache);
        SaveTaskbar(Path.Combine(outDir, "v3d-taskbar-light.png"), Color.FromRgb(0xE9, 0xE9, 0xEC), false, cache);
        SaveZoom(Path.Combine(outDir, "v3d-zoom-16-8x.png"), 16, 8, cache);

        Console.WriteLine($"[v3c] 输出目录: {outDir}");
        foreach (int size in saved)
        {
            Console.WriteLine($"  v3d-{size,-4} {new FileInfo(Path.Combine(outDir, $"v3d-{size}.png")).Length,9:N0} B");
        }

        return 0;
    }

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
        const double padding = 24;
        const double column = 280;
        double width = padding * 2 + column * Columns.Length;
        const double height = 430;

        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(background), null, new Rect(0, 0, width, height));
            Brush labelBrush = new SolidColorBrush(dark
                ? Color.FromRgb(0xE8, 0xE8, 0xE8)
                : Color.FromRgb(0x2A, 0x2A, 0x2A));

            for (int i = 0; i < Columns.Length; i++)
            {
                (string key, string label) = Columns[i];
                double centerX = padding + column * i + column / 2.0;

                if (key == "sandman")
                {
                    RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
                    dc.DrawImage(cache[("sandman", 32)], new Rect(centerX - 96, 40, 192, 192));
                    dc.DrawImage(cache[("sandman", 64)], new Rect(centerX - 32, 244, 64, 64));
                    RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
                }
                else
                {
                    dc.DrawImage(cache[(key, 256)], new Rect(centerX - 128, 22, 256, 256));

                    double rowX = centerX - (64 + 12 + 32 + 12 + 16) / 2.0;
                    dc.DrawImage(cache[(key, 64)], new Rect(rowX, 292, 64, 64));
                    rowX += 76;
                    dc.DrawImage(cache[(key, 32)], new Rect(rowX, 308, 32, 32));
                    rowX += 44;
                    dc.DrawImage(cache[(key, 16)], new Rect(rowX, 316, 16, 16));
                }

                FormattedText title = Text(label, 14, labelBrush, FontWeights.SemiBold);
                dc.DrawText(title, new Point(centerX - title.Width / 2.0, 382));
            }
        }

        Save(visual, path, width, height);
    }

    private static void SaveTaskbar(
        string path, Color background, bool dark, IReadOnlyDictionary<(string Key, int Size), BitmapSource> cache)
    {
        string[] rows = { "v3d", "a4b" };
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
        string[] keys = { "v3d", "a4b", "sandman" };
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
                BitmapSource image = keys[i] == "sandman"
                    ? cache[("sandman", 32)]
                    : cache[(keys[i], size)];
                dc.DrawImage(image, new Rect(x, padding, cell, cell));
                FormattedText text = Text($"{keys[i].ToUpperInvariant()}", 12, labelBrush, FontWeights.Normal);
                dc.DrawText(text, new Point(x, padding + cell + 6));
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