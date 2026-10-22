using System.Windows;
using System.Windows.Media;

namespace Custodian.IconForge;

/// <summary>
/// Direction A4: the old "C + box" semantics executed in the v2 graphite language.
/// A very bold ring (15.5% stroke) opened 62 degrees slightly below due east, with
/// the whole ring nudged up so the mark does not read as a copyright or spinner,
/// and a solid core square tucked into the lower right of the ring.
/// </summary>
internal static class LogoA4
{
    private const double S = 1024.0;
    private const double Center = 512.0;

    private const double RingDiameterRatio = 0.66;
    private const double StrokeRatio = 0.155;
    private const double CoreSideRatio = 0.155;
    private const double CoreRadiusRatio = 0.038;
    private const double CoreOffsetRatio = 0.012;

    private const double GapAngleDegrees = 62.0;
    private const double GapCenterDegrees = 4.0;
    private const double RingShiftRatio = -0.008;

    // 16 / 20 px: heavier stroke, larger ring and core so the ring stays >= 2 px.
    private const double CompactRingDiameterRatio = 0.78;
    private const double CompactStrokeRatio = 0.19;
    private const double CompactCoreRatio = 0.19;

    private static readonly Brush White = Freeze(new SolidColorBrush(Colors.White));

    public static void Draw(DrawingContext dc, int size, double gapAngleDegrees = GapAngleDegrees, bool inkBlue = false)
    {
        LogoA2.DrawPlate(dc, size, inkBlue);

        bool compact = size <= 20;
        double ringDiameter = (compact ? CompactRingDiameterRatio : RingDiameterRatio) * S;
        double stroke = (compact ? CompactStrokeRatio : StrokeRatio) * S;
        double core = (compact ? CompactCoreRatio : CoreSideRatio) * S;
        double coreRadius = (compact ? CompactCoreRatio * 0.22 : CoreRadiusRatio) * S;

        double cy = Center + RingShiftRatio * S;
        double outerRadius = ringDiameter / 2.0;
        double innerRadius = outerRadius - stroke;

        double gapHalf = gapAngleDegrees / 2.0 * Math.PI / 180.0;
        double gapCenter = GapCenterDegrees * Math.PI / 180.0;
        double start = gapCenter + gapHalf;
        double end = gapCenter - gapHalf + 2.0 * Math.PI;

        Point Outer(double angle) => new(Center + outerRadius * Math.Cos(angle), cy + outerRadius * Math.Sin(angle));
        Point Inner(double angle) => new(Center + innerRadius * Math.Cos(angle), cy + innerRadius * Math.Sin(angle));

        var ring = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (StreamGeometryContext ctx = ring.Open())
        {
            ctx.BeginFigure(Outer(start), isFilled: true, isClosed: true);
            ctx.ArcTo(Outer(end), new Size(outerRadius, outerRadius), 0.0, isLargeArc: true,
                SweepDirection.Clockwise, isStroked: true, isSmoothJoin: false);
            ctx.LineTo(Inner(end), isStroked: true, isSmoothJoin: false);
            ctx.ArcTo(Inner(start), new Size(innerRadius, innerRadius), 0.0, isLargeArc: true,
                SweepDirection.Counterclockwise, isStroked: true, isSmoothJoin: false);
        }

        ring.Freeze();
        dc.DrawGeometry(White, null, ring);

        double coreCx = Center + CoreOffsetRatio * S;
        double coreCy = cy + CoreOffsetRatio * S;
        var coreGeometry = new RectangleGeometry(
            new Rect(coreCx - core / 2.0, coreCy - core / 2.0, core, core), coreRadius, coreRadius);
        coreGeometry.Freeze();

        dc.DrawGeometry(White, null, coreGeometry);
    }

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}