using System.Windows;
using System.Windows.Media;

namespace Custodian.IconForge;

/// <summary>Candidate marks for the v2 logo review.</summary>
internal enum LogoConcept
{
    /// <summary>Graphite plate + thick square box outline (notch on top edge) + solid core square.</summary>
    A,

    /// <summary>Graphite plate + bold open ring (45 degree opening, thick bottom) + solid core square.</summary>
    B,

    /// <summary>Graphite plate + three offset rounded tiles, frontmost in the accent colour.</summary>
    C,

    /// <summary>Current shipped mark, kept only for side-by-side comparison sheets.</summary>
    Old,
}

/// <summary>
/// Logo v2 exploration: one graphite squircle plate and three symbol directions,
/// all drawn in the same 1024 unit design space with per-size optical tuning.
/// </summary>
internal static class LogoV2
{
    public static readonly Color Accent = Color.FromRgb(0x60, 0xCD, 0xFF);

    private const double Center = 512.0;

    private static readonly Brush PlateBrush = CreatePlateBrush();

    // Restrained 8% white inner highlight at the very top, nothing else.
    private static readonly Brush TopHighlight = CreateTopHighlight();

    private static readonly Brush White = Freeze(new SolidColorBrush(Colors.White));
    private static readonly Brush AccentBrush = Freeze(new SolidColorBrush(Accent));

    private static readonly Brush LayerBack3 = Freeze(new SolidColorBrush(Color.FromArgb(0x6B, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush LayerMid3 = Freeze(new SolidColorBrush(Color.FromArgb(0xC4, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush LayerBack2 = Freeze(new SolidColorBrush(Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF)));

    public static void Draw(DrawingContext dc, LogoConcept concept, int targetPixelSize)
    {
        if (concept == LogoConcept.Old)
        {
            IconDesign.Draw(dc, targetPixelSize);
            return;
        }

        DrawPlate(dc, targetPixelSize);

        switch (concept)
        {
            case LogoConcept.A:
                DrawBox(dc, targetPixelSize);
                break;
            case LogoConcept.B:
                DrawOpenRing(dc, targetPixelSize);
                break;
            case LogoConcept.C:
                DrawStack(dc, targetPixelSize);
                break;
        }
    }

    // ---------------------------------------------------------------- plate --

    private static void DrawPlate(DrawingContext dc, int targetPixelSize)
    {
        Geometry plate = IconDesign.CreatePlateGeometry(PlateHalf(targetPixelSize));
        dc.DrawGeometry(PlateBrush, null, plate);
        dc.DrawGeometry(TopHighlight, null, plate);
    }

    private static double PlateHalf(int size) => size switch
    {
        <= 16 => 512.0,   // full bleed: edges land on the pixel grid
        <= 20 => 508.0,
        _ => 500.0,
    };

    // ------------------------------------------------------- size behaviour --

    /// <summary>Small sizes get a slightly larger symbol so the glyph keeps its footprint.</summary>
    private static double SymbolScale(int size) => size switch
    {
        <= 16 => 1.20,
        <= 20 => 1.14,
        <= 24 => 1.08,
        <= 32 => 1.03,
        _ => 1.00,
    };

    /// <summary>Minimum stroke width in device pixels; forces >= 2 px below 24 px.</summary>
    private static double MinStrokePx(int size) => size switch
    {
        <= 16 => 2.05,
        <= 20 => 2.20,
        <= 24 => 2.40,
        <= 32 => 2.60,
        _ => 0.0,
    };

    private static double StrokeUnits(int size, double designUnits) =>
        Math.Max(designUnits, MinStrokePx(size) * 1024.0 / size);

    private static double CoreScale(int size) => size switch
    {
        <= 20 => 0.82,
        <= 24 => 0.90,
        _ => 1.00,
    };

    // ------------------------------------------------------------- concept A --

    /// <summary>
    /// Thick rounded square outline (11% of its side) with a solid rounded square
    /// held inside; above 32 px the top edge gets a centred slot so the box reads
    /// as "lid slightly open". Never optically hollow at 16 px.
    /// </summary>
    private static void DrawBox(DrawingContext dc, int size)
    {
        double scale = SymbolScale(size);
        double side = 620.0 * scale;
        double stroke = StrokeUnits(size, 68.0 * scale);
        double radius = 132.0 * scale;

        double innerSide = side - 2.0 * stroke;
        double innerRadius = Math.Max(4.0, radius - stroke);

        Geometry ring = Geometry.Combine(
            RoundRect(Center, Center, side, radius),
            RoundRect(Center, Center, innerSide, innerRadius),
            GeometryCombineMode.Exclude,
            null);

        if (size >= 32)
        {
            double top = Math.Max(stroke * 2.05, 110.0);
            double bottom = top * 0.74;
            double y0 = Center - side / 2.0 - 8.0;
            double y1 = Center - side / 2.0 + stroke + 8.0;

            var slot = new StreamGeometry { FillRule = FillRule.Nonzero };
            using (StreamGeometryContext ctx = slot.Open())
            {
                ctx.BeginFigure(new Point(Center - top / 2.0, y0), isFilled: true, isClosed: true);
                ctx.LineTo(new Point(Center + top / 2.0, y0), isStroked: true, isSmoothJoin: false);
                ctx.LineTo(new Point(Center + bottom / 2.0, y1), isStroked: true, isSmoothJoin: false);
                ctx.LineTo(new Point(Center - bottom / 2.0, y1), isStroked: true, isSmoothJoin: false);
            }

            slot.Freeze();
            ring = Geometry.Combine(ring, slot, GeometryCombineMode.Exclude, null);
        }

        dc.DrawGeometry(White, null, ring);

        double core = 252.0 * scale * CoreScale(size);
        double coreRadius = Math.Min(46.0 * scale * CoreScale(size), core * 0.20);
        dc.DrawGeometry(White, null, RoundRect(Center, Center, core, coreRadius));
    }

    // ------------------------------------------------------------- concept B --

    /// <summary>
    /// Bold open ring: constant outer radius, inner radius tapering so the arms are
    /// thin at the top and thick at the bottom, opening cut on the 45 degree diagonal.
    /// </summary>
    private static void DrawOpenRing(DrawingContext dc, int size)
    {
        double scale = SymbolScale(size);
        double outerRadius = 304.0 * scale;
        double baseThickness = 74.0 * scale;
        double variation = (size >= 48 ? 24.0 : size >= 24 ? 16.0 : 6.0) * scale;
        double minimum = MinStrokePx(size) * 1024.0 / size;
        double core = 168.0 * scale * CoreScale(size);

        const double gapCenter = -45.0 * Math.PI / 180.0;
        const double gapHalf = 36.0 * Math.PI / 180.0;

        double Thickness(double angle)
        {
            double thickness = baseThickness + variation * Math.Sin(angle);
            return Math.Max(thickness, minimum);
        }

        Point Outer(double angle) => new(
            Center + outerRadius * Math.Cos(angle),
            Center + outerRadius * Math.Sin(angle));

        Point Inner(double angle)
        {
            double radius = outerRadius - Thickness(angle);
            return new Point(Center + radius * Math.Cos(angle), Center + radius * Math.Sin(angle));
        }

        double start = gapCenter + gapHalf;
        double end = gapCenter - gapHalf + 2.0 * Math.PI;

        var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(Outer(start), isFilled: true, isClosed: true);
            ctx.ArcTo(Outer(end), new Size(outerRadius, outerRadius), 0.0, isLargeArc: true,
                SweepDirection.Clockwise, isStroked: true, isSmoothJoin: false);
            ctx.LineTo(Inner(end), isStroked: true, isSmoothJoin: false);

            // Inner boundary: from the second cut face back to the first one.
            const int steps = 160;
            for (int i = 1; i <= steps; i++)
            {
                double angle = end - (end - start) * i / steps;
                ctx.LineTo(Inner(angle), isStroked: true, isSmoothJoin: true);
            }
        }

        geometry.Freeze();
        dc.DrawGeometry(White, null, geometry);

        dc.DrawGeometry(White, null, RoundRect(Center, Center, core, core * 0.26));
    }

    // ------------------------------------------------------------- concept C --

    /// <summary>
    /// Three rounded tiles stepping up to the right: the two behind are dimmed
    /// white, the frontmost one carries the accent colour. Below 24 px it degrades
    /// to two tiles so the 16 px slot stays readable.
    /// </summary>
    private static void DrawStack(DrawingContext dc, int size)
    {
        double scale = SymbolScale(size);
        bool compact = size <= 20;
        int layers = compact ? 2 : 3;

        double side = 450.0 * scale * (compact ? 1.08 : 1.0);
        double radius = 104.0 * scale;
        double offset = (compact ? 108.0 : 66.0) * scale;
        double shift = offset * (layers - 1) / 2.0;

        for (int i = layers - 1; i >= 0; i--)
        {
            double cx = Center + shift - offset * i;
            double cy = Center - shift + offset * i;

            Brush brush = i switch
            {
                0 => AccentBrush,
                1 => layers == 3 ? LayerMid3 : LayerBack2,
                _ => LayerBack3,
            };

            dc.DrawGeometry(brush, null, RoundRect(cx, cy, side, radius));
        }
    }

    // ---------------------------------------------------------------- helpers --

    private static Geometry RoundRect(double cx, double cy, double side, double radius)
    {
        var geometry = new RectangleGeometry(
            new Rect(cx - side / 2.0, cy - side / 2.0, side, side),
            Math.Max(0.0, radius),
            Math.Max(0.0, radius));
        geometry.Freeze();
        return geometry;
    }

    private static Brush CreatePlateBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0.18, 0.0),
            EndPoint = new Point(0.82, 1.0),
        };
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x3B, 0x3D, 0x45), 0.00));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x25, 0x26, 0x2B), 0.52));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x14, 0x15, 0x19), 1.00));
        brush.Freeze();
        return brush;
    }

    private static Brush CreateTopHighlight()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0.0, 0.0),
            EndPoint = new Point(0.0, 1.0),
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF), 0.00));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.30));
        brush.Freeze();
        return brush;
    }

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}