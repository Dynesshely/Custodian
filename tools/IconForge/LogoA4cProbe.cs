using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Custodian.IconForge;

/// <summary>Compares A4c clearance variants (gap angle / core size) side by side.</summary>
internal static class LogoA4cProbe
{
    private static readonly (string Label, double Gap, double Core, double Offset)[] Variants =
    {
        ("54° 原规格（无间隙）", 54.0, 0.235, 0.30),
        ("62° / 0.225S / 0.31S", 62.0, 0.225, 0.31),
        ("68° / 0.225S / 0.315S", 68.0, 0.225, 0.315),
        ("70° / 0.225S / 0.31S", 70.0, 0.225, 0.31),
    };

    public static int Run(CliOptions options)
    {
        string outDir = options.Get("out") is { Length: > 0 } custom
            ? Path.GetFullPath(custom)
            : Path.Combine(Path.GetTempPath(), "custodian-logo-v2");
        Directory.CreateDirectory(outDir);

        const int size = 256;
        const double cell = 280;

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        double width = cell * Variants.Length;
        double height = cell + 40;

        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x1B, 0x1C, 0x1F)), null, new Rect(0, 0, width, height));
            Brush labelBrush = new SolidColorBrush(Color.FromRgb(0xD8, 0xD8, 0xDC));

            for (int i = 0; i < Variants.Length; i++)
            {
                (string label, double gap, double core, double offset) = Variants[i];
                dc.DrawImage(
                    LogoA4cSheets.RenderVariant(size, gap, core, offset),
                    new Rect(cell * i + 12, 12, size, size));
                dc.DrawText(
                    new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                        new Typeface("Segoe UI"), 12, labelBrush, 1.0),
                    new Point(cell * i + 12, size + 18));
            }
        }

        string path = Path.Combine(outDir, "a4c-clearance-study.png");
        var target = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        IconRender.SavePng(target, path);
        Console.WriteLine($"[a4c-probe] {path}");
        return 0;
    }
}
