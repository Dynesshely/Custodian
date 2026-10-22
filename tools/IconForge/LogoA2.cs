using System.Windows;
using System.Windows.Media;

namespace Custodian.IconForge;

/// <summary>
/// Direction A, refined: graphite squircle plate, a wide white box outline with
/// a released lid. Two variants share every proportion:
///   A2 - an independent lid tile floating above a gap in the box top edge;
///   A3 - the same box with a 45 degree chamfer cut from its top right corner and
///        a thin diagonal bar hinting at the lifted lid (negative space instead).
/// All ratios are expressed against S = 1024 design units, per the approved spec.
/// </summary>
internal static class LogoA2
{
    private const double S = 1024.0;
    private const double Center = 512.0;

    // Spec ratios (S = 1024).
    private const double BoxWidthRatio = 0.62;
    private const double BoxHeightRatio = 0.50;
    private const double BoxRadiusRatio = 0.075;
    private const double StrokeRatio = 0.082;
    private const double GapWidthRatio = 0.24;
    private const double GapDepthRatio = 0.085;
    private const double LidWidthRatio = 0.40;
    private const double LidThicknessRatio = 0.072;
    private const double LidRadiusRatio = 0.03;
    private const double LidGapRatio = 0.05;
    private const double LidTiltDegrees = 4.0;
    private const double CoreSideRatio = 0.15;
    private const double CoreRadiusRatio = 0.032;
    private const double CoreOffsetRatio = 0.03;
    private const double BoxCenterOffsetRatio = 0.02;

    // Small variant (16 / 20 px): closed ring, thicker stroke, bigger core.
    private const double CompactStrokeRatio = 0.10;
    private const double CompactCoreRatio = 0.19;

    private static readonly Brush PlateBrush = CreatePlateBrush();
    private static readonly Brush PlateBrushInkBlue = CreateInkBlueBrush();
    private static readonly Brush TopHighlight = Freeze(new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush White = Freeze(new SolidColorBrush(Colors.White));
    private static readonly Brush WhiteSoft = Freeze(new SolidColorBrush(Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF)));

    public static void Draw(DrawingContext dc, int size, bool chamferVariant)
    {
        DrawPlate(dc, size);
        if (size <= 20)
        {
            DrawCompact(dc, size);
            return;
        }

        // The chamfer removes mass from the top right corner, so the whole glyph
        // is nudged up to keep the optical centre on the canvas centre.
        if (chamferVariant)
        {
            dc.PushTransform(new TranslateTransform(0.0, -0.022 * S));
        }

        Metrics m = ForSize(size);
        double cx = Center;
        double cy = m.BoxCenterY;
        double halfW = m.BoxW / 2.0;
        double halfH = m.BoxH / 2.0;

        var outer = RoundRect(cx, cy, m.BoxW, m.BoxH, m.BoxR);
        var inner = RoundRect(
            cx, cy,
            m.BoxW - 2.0 * m.Stroke,
            m.BoxH - 2.0 * m.Stroke,
            Math.Max(m.BoxR - m.Stroke, 0.028 * S));

        Geometry ring = Geometry.Combine(outer, inner, GeometryCombineMode.Exclude, null);

        if (chamferVariant)
        {
            ring = Geometry.Combine(ring, ChamferCut(cx, cy, halfW, halfH), GeometryCombineMode.Exclude, null);
        }
        else
        {
            var gap = new RectangleGeometry(new Rect(
                cx - m.GapW / 2.0,
                cy - halfH - 12.0,
                m.GapW,
                m.GapD + 12.0));
            ring = Geometry.Combine(ring, gap, GeometryCombineMode.Exclude, null);
        }

        dc.DrawGeometry(White, null, ring);

        // Core square: solid, sitting slightly below the box centre.
        double coreCx = cx;
        double coreCy = cy + m.CoreOffset;
        dc.DrawGeometry(White, null, RoundRect(coreCx, coreCy, m.CoreSide, m.CoreSide, m.CoreR));

        if (chamferVariant)
        {
            DrawChamferBar(dc, cx, cy, halfW, halfH);
            dc.Pop();
        }
        else
        {
            DrawLid(dc, cx, cy, halfH, m);
        }
    }

    // -------------------------------------------------------------- geometry --

    private static void DrawLid(DrawingContext dc, double cx, double cy, double halfH, Metrics m)
    {
        double left = cx - m.LidW / 2.0;
        double bottom = cy - halfH - m.LidGap;
        double centerY = bottom - m.LidT / 2.0;

        var lid = new RectangleGeometry(
            new Rect(left, centerY - m.LidT / 2.0, m.LidW, m.LidT), m.LidR, m.LidR);
        lid.Transform = new RotateTransform(-LidTiltDegrees, left, centerY);
        lid.Freeze();

        dc.DrawGeometry(WhiteSoft, null, lid);
    }

    /// <summary>Half plane above the 45 degree chamfer, used to cut the corner.</summary>
    private static Geometry ChamferCut(double cx, double cy, double halfW, double halfH)
    {
        double chamfer = 0.17 * S;
        double right = cx + halfW;
        double top = cy - halfH;

        var p1 = new Point(right - chamfer, top);
        var p2 = new Point(right, top + chamfer);
        var normal = new Vector(0.70710678, -0.70710678);
        const double far = 2048.0;

        var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(p1, isFilled: true, isClosed: true);
            ctx.LineTo(p2, isStroked: false, isSmoothJoin: false);
            ctx.LineTo(p2 + normal * far, isStroked: false, isSmoothJoin: false);
            ctx.LineTo(p1 + normal * far, isStroked: false, isSmoothJoin: false);
        }

        geometry.Freeze();
        return geometry;
    }

    private static void DrawChamferBar(DrawingContext dc, double cx, double cy, double halfW, double halfH)
    {
        double chamfer = 0.17 * S;
        double right = cx + halfW;
        double top = cy - halfH;

        var p1 = new Point(right - chamfer, top);
        var p2 = new Point(right, top + chamfer);
        var normal = new Vector(0.70710678, -0.70710678);
        Point mid = new Point((p1.X + p2.X) / 2.0, (p1.Y + p2.Y) / 2.0) + normal * (0.024 * S);

        double length = 0.30 * S;
        double thickness = 0.040 * S;
        double radius = thickness / 2.0;

        var bar = new RectangleGeometry(
            new Rect(mid.X - length / 2.0, mid.Y - thickness / 2.0, length, thickness), radius, radius);
        bar.Transform = new RotateTransform(45.0, mid.X, mid.Y);
        bar.Freeze();

        dc.DrawGeometry(WhiteSoft, null, bar);
    }

    private static void DrawCompact(DrawingContext dc, int size)
    {
        double stroke = CompactStrokeRatio * S;
        double boxW = BoxWidthRatio * S;
        double boxH = BoxHeightRatio * S;
        double core = CompactCoreRatio * S;

        // Snap the outline to whole device pixels so the 16 px raster has no grey edges.
        double unit = S / size;
        boxW = Math.Round(boxW / unit) * unit;
        boxH = Math.Round(boxH / unit) * unit;
        stroke = Math.Round(stroke / unit) * unit;
        core = Math.Round(core / unit) * unit;

        // Keep at least 1 device pixel of clearance around the core square, and
        // keep the outline an even number of pixels so every edge lands on a
        // pixel boundary (no grey edges on the 16 px raster).
        double holeGap = boxH - 2.0 * stroke - core;
        while (holeGap < 2.0 * unit)
        {
            boxH += 2.0 * unit;
            holeGap = boxH - 2.0 * stroke - core;
        }

        double radius = Math.Max(0.0, Math.Round(BoxRadiusRatio * S / unit) * unit);
        double innerRadius = Math.Max(radius - stroke, 0.028 * S);

        Geometry ring = Geometry.Combine(
            RoundRect(Center, Center, boxW, boxH, radius),
            RoundRect(Center, Center, boxW - 2.0 * stroke, boxH - 2.0 * stroke, innerRadius),
            GeometryCombineMode.Exclude,
            null);

        dc.DrawGeometry(White, null, ring);
        dc.DrawGeometry(White, null, RoundRect(Center, Center + CoreOffsetRatio * S, core, core, core * 0.18));
    }

    // ---------------------------------------------------------------- plate ---

    internal static void DrawPlate(DrawingContext dc, int size, bool inkBlue = false)
    {
        double half = size <= 20 ? 512.0 : size <= 24 ? 508.0 : 500.0;
        Geometry plate = IconDesign.CreatePlateGeometry(half);

        dc.DrawGeometry(inkBlue ? PlateBrushInkBlue : PlateBrush, null, plate);

        if (size >= 32)
        {
            // 1 device pixel inner highlight along the very top edge (8% white).
            double band = Math.Max(3.0, S / size);
            dc.PushClip(plate);
            dc.DrawRectangle(TopHighlight, null, new Rect(0.0, 0.0, S, band));
            dc.Pop();
        }
    }

    // --------------------------------------------------------------- metrics --

    private readonly record struct Metrics(
        double BoxW,
        double BoxH,
        double BoxR,
        double Stroke,
        double GapW,
        double GapD,
        double LidW,
        double LidT,
        double LidR,
        double LidGap,
        double CoreSide,
        double CoreR,
        double CoreOffset,
        double BoxCenterY);

    private static Metrics ForSize(int size)
    {
        double stroke = StrokeRatio * S;
        if (size <= 24)
        {
            stroke += S / size; // +1 device pixel of weight at 24 px
        }

        double gapDepth = Math.Max(GapDepthRatio * S, stroke * 1.15);

        return new Metrics(
            BoxW: BoxWidthRatio * S,
            BoxH: BoxHeightRatio * S,
            BoxR: BoxRadiusRatio * S,
            Stroke: stroke,
            GapW: GapWidthRatio * S,
            GapD: gapDepth,
            LidW: LidWidthRatio * S,
            LidT: LidThicknessRatio * S,
            LidR: LidRadiusRatio * S,
            LidGap: LidGapRatio * S,
            CoreSide: CoreSideRatio * S,
            CoreR: CoreRadiusRatio * S,
            CoreOffset: CoreOffsetRatio * S,
            BoxCenterY: Center + BoxCenterOffsetRatio * S);
    }

    // --------------------------------------------------------------- helpers --

    private static Geometry RoundRect(double cx, double cy, double sideX, double sideY, double radius)
    {
        var geometry = new RectangleGeometry(
            new Rect(cx - sideX / 2.0, cy - sideY / 2.0, sideX, sideY),
            Math.Max(0.0, radius),
            Math.Max(0.0, radius));
        geometry.Freeze();
        return geometry;
    }

    private static Brush CreatePlateBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0.0, 0.0),
            EndPoint = new Point(0.0, 1.0),
        };
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x3B, 0x42, 0x50), 0.0));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x20, 0x24, 0x2B), 1.0));
        brush.Freeze();
        return brush;
    }

    private static Brush CreateInkBlueBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0.0, 0.0),
            EndPoint = new Point(0.0, 1.0),
        };
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x22, 0x30, 0x4A), 0.0));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x13, 0x1C, 0x2E), 0.55));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x0A, 0x0F, 0x19), 1.0));
        brush.Freeze();
        return brush;
    }

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}