using System.Windows;
using System.Windows.Media;

namespace Custodian.IconForge;

/// <summary>
/// v3b: the approved sandbox-family mark. A blue squircle plate, a white box
/// (rim + tapered body) holding a gold sand heap that spills over the rim, with
/// two small white app tiles sunk into the sand. At 16/20 px the rim and body
/// merge and the sand becomes a single arched band (the SandMan-32 recipe).
/// </summary>
internal static class LogoV3b
{
    private const double S = 1024.0;
    private const double Center = 512.0;

    // Vertical layout (all ratios of S).
    private const double RimTopRatio = 0.44;
    private const double RimThicknessRatio = 0.075;
    private const double RimWidthRatio = 0.60;
    private const double RimRadiusRatio = 0.025;

    private const double GapRatio = 0.018;
    private const double BodyTopWidthRatio = 0.56;
    private const double BodyBottomWidthRatio = 0.50;
    private const double BodyHeightRatio = 0.225;
    private const double BodyRadiusRatio = 0.03;

    private const double SandWidthRatio = 0.46;
    private const double SandHeightRatio = 0.15;
    private const double SandArchRatio = 0.02;
    private const double SandTopRadiusRatio = 0.06;
    private const double SandBottomRadiusRatio = 0.075;
    private const double SandSpillRatio = 0.015;   // gold peeking under the rim
    private const double SandTuckRatio = 0.012;    // how far it sits into the rim

    private const double TileSizeRatio = 0.075;
    private const double TileRadiusRatio = 0.02;
    private const double TileGapRatio = 0.05;
    private const double TileSinkRatio = 0.008;    // tiles half buried by the rim

    private static readonly Color Sandgold = Color.FromRgb(0xF5, 0xC3, 0x42);
    private static readonly Brush White = Freeze(new SolidColorBrush(Colors.White));
    private static readonly Brush Gold = Freeze(new SolidColorBrush(Sandgold));

    public static void Draw(DrawingContext dc, int size)
    {
        DrawPlate(dc, size);

        if (size <= 20)
        {
            DrawCompact(dc, size);
            return;
        }

        double rimTop = RimTopRatio * S;
        double rimH = RimThicknessRatio * S;
        double rimW = RimWidthRatio * S;
        double rimBottom = rimTop + rimH;

        double bodyTop = rimBottom + GapRatio * S;
        double bodyH = BodyHeightRatio * S;
        double bodyBottom = bodyTop + bodyH;

        // 1) Sand first so the rim covers its lower edge, with a thin spill below.
        double sandW = SandWidthRatio * S;
        double sandH = SandHeightRatio * S;
        double sandBottom = rimBottom + SandSpillRatio * S;
        double sandTop = sandBottom - sandH;

        dc.DrawGeometry(Gold, null, SandGeometry(Center, sandW, sandTop, sandH));

        // 2) Box body (tapered, rounded corners).
        double bodyTopW = BodyTopWidthRatio * S;
        double bodyBottomW = BodyBottomWidthRatio * S;
        double bodyRadius = BodyRadiusRatio * S;

        var bodyPoints = new[]
        {
            new Point(Center - bodyTopW / 2.0, bodyTop),
            new Point(Center + bodyTopW / 2.0, bodyTop),
            new Point(Center + bodyBottomW / 2.0, bodyBottom),
            new Point(Center - bodyBottomW / 2.0, bodyBottom),
        };

        dc.DrawGeometry(White, null, RoundedPolygon(bodyPoints, bodyRadius));

        // 3) Rim in front of the sand.
        var rim = new RectangleGeometry(
            new Rect(Center - rimW / 2.0, rimTop, rimW, rimH), RimRadiusRatio * S, RimRadiusRatio * S);
        rim.Freeze();
        dc.DrawGeometry(White, null, rim);

        // 4) App tiles: drawn last so their lower half sinks into the rim.
        double tile = TileSizeRatio * S;
        double gap = TileGapRatio * S;
        double totalW = tile * 2.0 + gap;
        double tileTop = rimTop - tile / 2.0 - TileSinkRatio * S;

        var tileGeometry = new RectangleGeometry(
            new Rect(0, 0, tile, tile), TileRadiusRatio * S, TileRadiusRatio * S);
        tileGeometry.Freeze();

        for (int i = 0; i < 2; i++)
        {
            double left = Center - totalW / 2.0 + i * (tile + gap);
            dc.PushTransform(new TranslateTransform(left, tileTop));
            dc.DrawGeometry(White, null, tileGeometry);
            dc.Pop();
        }
    }

    /// <summary>16 / 20 px: one merged white trapezoid plus a single gold band.</summary>
    private static void DrawCompact(DrawingContext dc, int size)
    {
        double unit = S / size;
        double Snap(double value) => Math.Round(value / unit) * unit;

        double top = Snap(RimTopRatio * S);
        double bodyH = Snap((RimThicknessRatio + GapRatio + BodyHeightRatio) * S);
        double topW = Snap(RimWidthRatio * S);
        double bottomW = Snap(BodyBottomWidthRatio * S);
        double radius = Snap(0.05 * S);

        var points = new[]
        {
            new Point(Center - topW / 2.0, top),
            new Point(Center + topW / 2.0, top),
            new Point(Center + bottomW / 2.0, top + bodyH),
            new Point(Center - bottomW / 2.0, top + bodyH),
        };

        dc.DrawGeometry(White, null, RoundedPolygon(points, radius));

        // Sand: 1.5-2 device pixels of gold at the top of the box, slightly arched.
        double bandH = Math.Max(1.5 * unit, Snap(0.09 * S));
        double bandW = Snap(SandWidthRatio * S);
        double bandTop = top + Math.Min(bandH * 0.45, 0.02 * S);

        var band = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (StreamGeometryContext ctx = band.Open())
        {
            double left = Center - bandW / 2.0;
            double right = Center + bandW / 2.0;
            double corner = Math.Min(bandH / 2.0, 0.02 * S);

            ctx.BeginFigure(new Point(left + corner, bandTop), isFilled: true, isClosed: true);
            ctx.QuadraticBezierTo(
                new Point(Center, bandTop - 0.02 * S), new Point(right - corner, bandTop), true, true);
            ctx.ArcTo(new Point(right, bandTop + corner), new Size(corner, corner), 0.0,
                isLargeArc: false, SweepDirection.Clockwise, true, false);
            ctx.LineTo(new Point(right, bandTop + bandH - corner), true, false);
            ctx.ArcTo(new Point(right - corner, bandTop + bandH), new Size(corner, corner), 0.0,
                isLargeArc: false, SweepDirection.Clockwise, true, false);
            ctx.LineTo(new Point(left + corner, bandTop + bandH), true, false);
            ctx.ArcTo(new Point(left, bandTop + bandH - corner), new Size(corner, corner), 0.0,
                isLargeArc: false, SweepDirection.Clockwise, true, false);
            ctx.LineTo(new Point(left, bandTop + corner), true, false);
            ctx.ArcTo(new Point(left + corner, bandTop), new Size(corner, corner), 0.0,
                isLargeArc: false, SweepDirection.Clockwise, true, false);
        }

        band.Freeze();
        dc.DrawGeometry(Gold, null, band);
    }

    /// <summary>Round-topped sand heap: arched top edge, rounder bottom corners.</summary>
    private static Geometry SandGeometry(double cx, double width, double top, double height)
    {
        double left = cx - width / 2.0;
        double right = cx + width / 2.0;
        double bottom = top + height;
        double topRadius = SandTopRadiusRatio * S;
        double bottomRadius = SandBottomRadiusRatio * S;

        var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(left + topRadius, top), isFilled: true, isClosed: true);
            ctx.QuadraticBezierTo(
                new Point(cx, top - SandArchRatio * S), new Point(right - topRadius, top), true, true);
            ctx.ArcTo(new Point(right, top + topRadius), new Size(topRadius, topRadius), 0.0,
                isLargeArc: false, SweepDirection.Clockwise, true, false);
            ctx.LineTo(new Point(right, bottom - bottomRadius), true, false);
            ctx.ArcTo(new Point(right - bottomRadius, bottom), new Size(bottomRadius, bottomRadius), 0.0,
                isLargeArc: false, SweepDirection.Clockwise, true, false);
            ctx.LineTo(new Point(left + bottomRadius, bottom), true, false);
            ctx.ArcTo(new Point(left, bottom - bottomRadius), new Size(bottomRadius, bottomRadius), 0.0,
                isLargeArc: false, SweepDirection.Clockwise, true, false);
            ctx.LineTo(new Point(left, top + topRadius), true, false);
            ctx.ArcTo(new Point(left + topRadius, top), new Size(topRadius, topRadius), 0.0,
                isLargeArc: false, SweepDirection.Clockwise, true, false);
        }

        geometry.Freeze();
        return geometry;
    }

    /// <summary>Convex polygon with every corner rounded by the same radius.</summary>
    private static Geometry RoundedPolygon(IReadOnlyList<Point> points, double radius)
    {
        var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
        int count = points.Count;

        Point Trim(Point vertex, Point neighbour)
        {
            Vector direction = neighbour - vertex;
            direction.Normalize();
            return vertex + direction * radius;
        }

        using (StreamGeometryContext ctx = geometry.Open())
        {
            Point start = Trim(points[0], points[1]);
            ctx.BeginFigure(start, isFilled: true, isClosed: true);

            for (int i = 1; i <= count; i++)
            {
                Point vertex = points[i % count];
                Point previous = points[(i - 1) % count];
                Point next = points[(i + 1) % count];

                ctx.LineTo(Trim(vertex, previous), true, false);
                ctx.ArcTo(Trim(vertex, next), new Size(radius, radius), 0.0,
                    isLargeArc: false, SweepDirection.Clockwise, true, false);
            }
        }

        geometry.Freeze();
        return geometry;
    }

    private static void DrawPlate(DrawingContext dc, int size)
    {
        double half = size <= 16 ? 512.0 : size <= 24 ? 508.0 : 500.0;
        Geometry plate = IconDesign.CreatePlateGeometry(half);

        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0.0, 0.0),
            EndPoint = new Point(0.0, 1.0),
        };
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x2E, 0x8B, 0xF7), 0.0));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x0F, 0x4C, 0x9C), 1.0));
        brush.Freeze();

        dc.DrawGeometry(brush, null, plate);

        if (size >= 32)
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

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
