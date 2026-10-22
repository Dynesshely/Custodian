using System.Windows;
using System.Windows.Media;

namespace Custodian.IconForge;

internal enum V3Palette
{
    /// <summary>Light plate (white to pale blue), Sandboxie blue box, gold sand.</summary>
    P1,

    /// <summary>Sandboxie blue plate, white box, gold sand.</summary>
    P2,

    /// <summary>Blue plate (lighter top, deeper bottom), white box, deeper gold.</summary>
    P3,
}

/// <summary>
/// v3 "sandbox family" mark: a shallow box (rim + trapezoid body) filled with a
/// gold sand heap; two small white app tiles sit on the sand, i.e. "apps running
/// inside the sandbox". Below 24 px the mark collapses to the SandMan-32 recipe:
/// rim + two side rails + one gold band.
/// </summary>
internal static class LogoV3
{
    private const double S = 1024.0;
    private const double Center = 512.0;

    private const double RimWidthRatio = 0.62;
    private const double RimThicknessRatio = 0.09;
    private const double RimTopRatio = 0.46;
    private const double BodyHeightRatio = 0.19;
    private const double BodyTaperRatio = 0.94;
    private const double SandWidthRatio = 0.52;
    private const double SandHeightRatio = 0.21;
    private const double SandRiseRatio = 0.17;   // how far the heap rises above the rim
    private const double SandTuckRatio = 0.04;   // how far its base hides behind the rim
    private const double TileSizeRatio = 0.09;
    private const double TileGapRatio = 0.06;

    private static readonly Color SandboxieBlue = Color.FromRgb(0x19, 0x76, 0xD2);
    private static readonly Color SandboxieBlueDark = Color.FromRgb(0x0D, 0x47, 0xA1);
    private static readonly Color SandGold = Color.FromRgb(0xF5, 0xC3, 0x42);
    private static readonly Color SandGoldDeep = Color.FromRgb(0xE8, 0xA8, 0x2A);

    private static readonly Brush White = Freeze(new SolidColorBrush(Colors.White));
    private static readonly Brush BlueMain = Freeze(new SolidColorBrush(SandboxieBlue));
    private static readonly Brush BlueDark = Freeze(new SolidColorBrush(SandboxieBlueDark));
    private static readonly Brush Gold = Freeze(new SolidColorBrush(SandGold));
    private static readonly Brush GoldDeep = Freeze(new SolidColorBrush(SandGoldDeep));

    public static void Draw(DrawingContext dc, int size, V3Palette palette)
    {
        DrawPlate(dc, size, palette);

        if (size <= 20)
        {
            DrawCompact(dc, size, palette);
            return;
        }

        double rimW = RimWidthRatio * S;
        double rimH = RimThicknessRatio * S;
        double rimTop = RimTopRatio * S;
        double bodyH = BodyHeightRatio * S;
        double bodyBottomW = rimW * BodyTaperRatio;
        double sandW = SandWidthRatio * S;
        double sandH = SandHeightRatio * S;
        double sandBottom = rimTop + SandTuckRatio * S;   // tucked behind the rim

        Brush bodyBrush = palette == V3Palette.P1 ? BlueDark : White;
        Brush rimBrush = palette == V3Palette.P1 ? BlueMain : White;
        Brush sandBrush = palette == V3Palette.P3 ? GoldDeep : Gold;

        // Sand heap first: its base hides behind the rim, like the Sandboxie mark.
        double sandRadius = sandW * 0.12;
        var sand = new RectangleGeometry(
            new Rect(Center - sandW / 2.0, sandBottom - sandH, sandW, sandH), sandRadius, sandRadius);
        sand.Freeze();
        dc.DrawGeometry(sandBrush, null, sand);

        // Body: trapezoid, 6% narrower at the bottom.
        var body = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (StreamGeometryContext ctx = body.Open())
        {
            ctx.BeginFigure(new Point(Center - rimW / 2.0, rimTop + rimH), isFilled: true, isClosed: true);
            ctx.LineTo(new Point(Center + rimW / 2.0, rimTop + rimH), isStroked: false, isSmoothJoin: false);
            ctx.LineTo(new Point(Center + bodyBottomW / 2.0, rimTop + rimH + bodyH), isStroked: false, isSmoothJoin: false);
            ctx.LineTo(new Point(Center - bodyBottomW / 2.0, rimTop + rimH + bodyH), isStroked: false, isSmoothJoin: false);
        }

        body.Freeze();
        dc.DrawGeometry(bodyBrush, null, body);

        // Rim: horizontal band with softly rounded ends, drawn in front of the sand.
        var rim = new RectangleGeometry(
            new Rect(Center - rimW / 2.0, rimTop, rimW, rimH), 0.02 * S, 0.02 * S);
        rim.Freeze();
        dc.DrawGeometry(rimBrush, null, rim);

        // Two app tiles resting on the visible part of the heap (dropped below 24 px).
        double tile = TileSizeRatio * S;
        double gap = TileGapRatio * S;
        double totalW = tile * 2.0 + gap;
        double visibleTop = rimTop - SandRiseRatio * S;
        double tileTop = visibleTop + (SandRiseRatio * S - tile) / 2.0;

        var tileGeometry = new RectangleGeometry(new Rect(0, 0, tile, tile), 0.022 * S, 0.022 * S);
        tileGeometry.Freeze();

        for (int i = 0; i < 2; i++)
        {
            double left = Center - totalW / 2.0 + i * (tile + gap);
            dc.PushTransform(new TranslateTransform(left, tileTop));
            dc.DrawGeometry(White, null, tileGeometry);
            dc.Pop();
        }
    }

    /// <summary>16 / 20 px: rim + two side rails + one gold band, exactly like SandMan-32.</summary>
    private static void DrawCompact(DrawingContext dc, int size, V3Palette palette)
    {
        double unit = S / size;
        double Snap(double value) => Math.Round(value / unit) * unit;

        double rimW = Snap(RimWidthRatio * S);
        double rimH = Math.Max(unit, Snap(0.11 * S));
        double rimTop = Snap(RimTopRatio * S - 0.02 * S);
        double railW = Math.Max(unit, Snap(0.11 * S));
        double railH = Snap(0.30 * S);
        double bandH = 1.5 * unit;

        Brush rimBrush = palette == V3Palette.P1 ? BlueDark : White;
        Brush sandBrush = palette == V3Palette.P3 ? GoldDeep : Gold;

        // Two side rails.
        var rail = new RectangleGeometry(new Rect(0, 0, railW, rimTop + railH));
        rail.Freeze();
        dc.PushTransform(new TranslateTransform(Center - rimW / 2.0, rimTop));
        dc.DrawGeometry(rimBrush, null, rail);
        dc.Pop();
        dc.PushTransform(new TranslateTransform(Center + rimW / 2.0 - railW, rimTop));
        dc.DrawGeometry(rimBrush, null, rail);
        dc.Pop();

        // Rim.
        var rim = new RectangleGeometry(new Rect(Center - rimW / 2.0, rimTop, rimW, rimH));
        rim.Freeze();
        dc.DrawGeometry(rimBrush, null, rim);

        // One gold band just under the rim.
        double bandW = rimW - 2.0 * railW;
        var band = new RectangleGeometry(new Rect(Center - bandW / 2.0, rimTop + rimH, bandW, bandH));
        band.Freeze();
        dc.DrawGeometry(sandBrush, null, band);
    }

    private static void DrawPlate(DrawingContext dc, int size, V3Palette palette)
    {
        double half = size <= 16 ? 512.0 : size <= 24 ? 508.0 : 500.0;
        Geometry plate = IconDesign.CreatePlateGeometry(half);

        dc.DrawGeometry(CreatePlateBrush(palette), null, plate);

        if (size >= 32 && palette != V3Palette.P1)
        {
            double band = Math.Max(3.0, S / size);
            dc.PushClip(plate);
            dc.DrawRectangle(
                Freeze(new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF))),
                null,
                new Rect(0.0, 0.0, S, band));
            dc.Pop();
        }
    }

    private static Brush CreatePlateBrush(V3Palette palette)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0.0, 0.0),
            EndPoint = new Point(0.0, 1.0),
        };

        switch (palette)
        {
            case V3Palette.P1:
                brush.GradientStops.Add(new GradientStop(Colors.White, 0.0));
                brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xEA, 0xF2, 0xFF), 1.0));
                break;

            case V3Palette.P2:
                brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x2E, 0x8B, 0xF7), 0.0));
                brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x0F, 0x4C, 0x9C), 1.0));
                break;

            default:
                brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x3D, 0x9B, 0xF5), 0.0));
                brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x0B, 0x3D, 0x7A), 1.0));
                break;
        }

        brush.Freeze();
        return brush;
    }

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}