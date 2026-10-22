using System.Windows;
using System.Windows.Media;

namespace Custodian.IconForge;

/// <summary>
/// v3c: Sandboxie-family mark, final tuning. Blue squircle plate, white box
/// (rim + tapered body with a structural seam), a gold sand heap that rises
/// ~0.075S above the rim and spills a sliver below it, and two white app tiles
/// standing in the visible part of the sand (32 px and up).
/// </summary>
internal static class LogoV3c
{
    public const double S = 1024.0;
    public const double Center = 512.0;

    public const double RimTopRatio = 0.44;
    public const double RimThicknessRatio = 0.062;
    public const double RimWidthRatio = 0.60;
    public const double RimRadiusRatio = 0.025;

    public const double SeamRatio = 0.018;
    public const double BodyTopWidthRatio = 0.56;
    public const double BodyBottomWidthRatio = 0.50;
    public const double BodyHeightRatio = 0.225;
    public const double BodyRadiusRatio = 0.03;

    public const double SandWidthRatio = 0.50;
    public const double SandVisibleRatio = 0.10;    // gold showing above the rim
    public const double SandSpillRatio = 0.008;     // gold sliver inside the 0.018S seam
    public const double SandArchRatio = 0.045;
    public const double SandTopRadiusRatio = 0.06;
    public const double SandBottomRadiusRatio = 0.075;

    public static readonly Color GoldColor = Color.FromRgb(0xF5, 0xC3, 0x42);
    public static readonly Color PlateTopColor = Color.FromRgb(0x2E, 0x8B, 0xF7);
    public static readonly Color PlateBottomColor = Color.FromRgb(0x0F, 0x4C, 0x9C);

    private static readonly Brush White = Freeze(new SolidColorBrush(Colors.White));
    private static readonly Brush Gold = Freeze(new SolidColorBrush(GoldColor));

    // ------------------------------------------------------------ drawing ----

    public static void Draw(DrawingContext dc, int size)
    {
        DrawPlate(dc, size);

        if (size <= 20)
        {
            DrawCompact(dc, size);
            return;
        }

        dc.DrawGeometry(Gold, null, CreateSandGeometry());
        dc.DrawGeometry(White, null, CreateBodyGeometry());
        dc.DrawGeometry(White, null, CreateRimGeometry());
    }

    /// <summary>16 / 20 px: rim and body merge, the sand becomes one arched band.</summary>
    private static void DrawCompact(DrawingContext dc, int size)
    {
        double unit = S / size;
        double Snap(double value) => Math.Round(value / unit) * unit;

        double top = Snap(RimTopRatio * S);
        double height = Snap((RimThicknessRatio + SeamRatio + BodyHeightRatio) * S);
        double topW = Snap(RimWidthRatio * S);
        double bottomW = Snap(BodyBottomWidthRatio * S);
        double radius = Snap(0.05 * S);

        var points = new[]
        {
            new Point(Center - topW / 2.0, top),
            new Point(Center + topW / 2.0, top),
            new Point(Center + bottomW / 2.0, top + height),
            new Point(Center - bottomW / 2.0, top + height),
        };

        dc.DrawGeometry(White, null, RoundedPolygon(points, radius));

        double bandH = Math.Max(1.8 * unit, Snap(0.10 * S));
        double bandW = Snap(SandWidthRatio * S);
        double bandTop = top + Math.Min(bandH * 0.4, 0.016 * S);

        dc.DrawGeometry(Gold, null, ArchedBand(Center, bandW, bandTop, bandH, Math.Min(bandH * 0.5, 0.02 * S)));
    }

    // ----------------------------------------------------------- geometry ----

    public static Geometry CreatePlateGeometry() => IconDesign.CreatePlateGeometry(500.0);

    public static Geometry CreateRimGeometry()
    {
        var rim = new RectangleGeometry(
            new Rect(Center - RimWidthRatio * S / 2.0, RimTopRatio * S, RimWidthRatio * S, RimThicknessRatio * S),
            RimRadiusRatio * S,
            RimRadiusRatio * S);
        rim.Freeze();
        return rim;
    }

    public static Geometry CreateBodyGeometry()
    {
        double bodyTop = (RimTopRatio + RimThicknessRatio + SeamRatio) * S;
        double bodyBottom = bodyTop + BodyHeightRatio * S;
        double topW = BodyTopWidthRatio * S;
        double bottomW = BodyBottomWidthRatio * S;

        var points = new[]
        {
            new Point(Center - topW / 2.0, bodyTop),
            new Point(Center + topW / 2.0, bodyTop),
            new Point(Center + bottomW / 2.0, bodyBottom),
            new Point(Center - bottomW / 2.0, bodyBottom),
        };

        return RoundedPolygon(points, BodyRadiusRatio * S);
    }

    public static Geometry CreateSandGeometry()
    {
        double rimTop = RimTopRatio * S;
        double rimBottom = rimTop + RimThicknessRatio * S;
        double top = rimTop - SandVisibleRatio * S;
        double bottom = rimBottom + SandSpillRatio * S;

        double left = Center - SandWidthRatio * S / 2.0;
        double right = Center + SandWidthRatio * S / 2.0;
        double topRadius = SandTopRadiusRatio * S;
        double bottomRadius = SandBottomRadiusRatio * S;

        var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(left + topRadius, top), isFilled: true, isClosed: true);
            ctx.QuadraticBezierTo(new Point(Center, top - SandArchRatio * S), new Point(right - topRadius, top), true, true);
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

    // ------------------------------------------------------------- helpers ---

    public static Geometry RoundedPolygon(IReadOnlyList<Point> points, double radius)
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
            ctx.BeginFigure(Trim(points[0], points[1]), isFilled: true, isClosed: true);

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

    private static Geometry ArchedBand(double cx, double width, double top, double height, double corner)
    {
        double left = cx - width / 2.0;
        double right = cx + width / 2.0;

        var band = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (StreamGeometryContext ctx = band.Open())
        {
            ctx.BeginFigure(new Point(left + corner, top), isFilled: true, isClosed: true);
            ctx.QuadraticBezierTo(new Point(cx, top - 0.02 * S), new Point(right - corner, top), true, true);
            ctx.LineTo(new Point(right, top + height), true, false);
            ctx.LineTo(new Point(left, top + height), true, false);
        }

        band.Freeze();
        return band;
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
        brush.GradientStops.Add(new GradientStop(PlateTopColor, 0.0));
        brush.GradientStops.Add(new GradientStop(PlateBottomColor, 1.0));
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