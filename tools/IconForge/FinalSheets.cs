using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Custodian.IconForge;

/// <summary>Produces the v3c hand-off images: 4-up contact sheet and taskbar strip.</summary>
internal static class FinalSheets
{
    private static readonly int[] Sizes = { 256, 64, 32, 24, 20, 16 };

    public static int Run(CliOptions options)
    {
        string outDir = options.Get("out") is { Length: > 0 } custom
            ? Path.GetFullPath(custom)
            : Path.Combine(Path.GetTempPath(), "custodian-logo-v2");
        Directory.CreateDirectory(outDir);

        var final = Sizes.ToDictionary(size => size, size => IconRender.Render(size));
        var previous = Sizes.ToDictionary(
            size => size,
            size => IconRender.RenderCustom((dc, s) => LogoA4.Draw(dc, s, 54.0, true), size));

        BitmapSource sandman32 = LoadReference("SandMan-32.png");
        BitmapSource sandman64 = LoadReference("SandMan-64.png");

        WriteContact(Path.Combine(outDir, "v3d-final-contact.png"), final, previous, sandman32, sandman64);
        WriteTaskbar(Path.Combine(outDir, "v3d-final-taskbar.png"), final, previous);

        Console.WriteLine($"[final-sheets] {Path.Combine(outDir, "v3d-final-contact.png")}");
        Console.WriteLine($"[final-sheets] {Path.Combine(outDir, "v3d-final-taskbar.png")}");
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

    private static void WriteContact(
        string path,
        IReadOnlyDictionary<int, BitmapSource> final,
        IReadOnlyDictionary<int, BitmapSource> previous,
        BitmapSource sandman32,
        BitmapSource sandman64)
    {
        const double padding = 22;
        const double column = 290;
        double width = padding * 2 + column * 4;
        const double height = 420;

        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x1B, 0x1C, 0x1F)), null, new Rect(0, 0, width, height));
            Brush labelBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));

            DrawHero(0, final[256], "v3c · 新版 256");
            DrawRow(1, final, "v3c · 64 / 32 / 16");

            double sandX = padding + column * 2 + column / 2.0;
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
            dc.DrawImage(sandman32, new Rect(sandX - 96, 40, 192, 192));
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
            dc.DrawImage(sandman64, new Rect(sandX - 32, 244, 64, 64));
            DrawLabel(sandX, "SandMan 参照（32 ×8 / 64）");

            DrawHero(3, previous[256], "旧版 A4b · 墨蓝 256");

            void DrawHero(int index, BitmapSource image, string label)
            {
                double centerX = padding + column * index + column / 2.0;
                dc.DrawImage(image, new Rect(centerX - 128, 26, 256, 256));
                DrawLabel(centerX, label);
            }

            void DrawRow(int index, IReadOnlyDictionary<int, BitmapSource> images, string label)
            {
                double centerX = padding + column * index + column / 2.0;
                double rowX = centerX - (64 + 12 + 32 + 12 + 16) / 2.0;
                dc.DrawImage(images[64], new Rect(rowX, 126, 64, 64));
                rowX += 76;
                dc.DrawImage(images[32], new Rect(rowX, 142, 32, 32));
                rowX += 44;
                dc.DrawImage(images[16], new Rect(rowX, 150, 16, 16));
                DrawLabel(centerX, label);
            }

            void DrawLabel(double centerX, string label)
            {
                FormattedText text = Text(label, 14, labelBrush, FontWeights.SemiBold);
                dc.DrawText(text, new Point(centerX - text.Width / 2.0, 360));
            }
        }

        Save(visual, path, width, height);
    }

    private static void WriteTaskbar(
        string path,
        IReadOnlyDictionary<int, BitmapSource> final,
        IReadOnlyDictionary<int, BitmapSource> previous)
    {
        (int Size, double X)[] row = { (32, 0), (24, 52), (20, 94), (16, 128) };
        const double labelWidth = 62;
        const double rowHeight = 52;
        double width = labelWidth + 176;
        double height = rowHeight * 2 + 16;

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)), null, new Rect(0, 0, width, height));
            Brush labelBrush = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xCC));

            DrawRow(0, "新版", final);
            DrawRow(1, "旧版", previous);

            void DrawRow(int index, string label, IReadOnlyDictionary<int, BitmapSource> images)
            {
                double top = 8 + rowHeight * index;
                FormattedText text = Text(label, 12, labelBrush, FontWeights.SemiBold);
                dc.DrawText(text, new Point(14, top + rowHeight / 2.0 - text.Height / 2.0));

                foreach ((int size, double x) in row)
                {
                    double y = top + (rowHeight - size) / 2.0;
                    dc.DrawImage(images[size], new Rect(labelWidth + x, y, size, size));
                }
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