using System.Windows;
using System.Windows.Media;

namespace Custodian.IconForge;

internal enum PlatePalette
{
    Graphite,
    InkBlue,
}

/// <summary>
/// A4c: the core square no longer floats in the middle - it is wedged into the
/// C's opening (left half inside the mouth, right half past the outer edge), so
/// the mark reads as "the custodian holds the thing at the door" instead of a
/// letter with a dot.
/// </summary>
internal static class LogoA4c
{
    private const double S = 1024.0;
    private const double Center = 512.0;

    private const double RingDiameterRatio = 0.68;
    private const double RingStrokeRatio = 0.15;
    private const double RingCenterLiftRatio = 0.005;

    private const double CoreSideRatio = 0.225;
    private const double CoreRadiusRatio = 0.055;
    private const double CoreOffsetRatio = 0.31;

    private const double GapAngleDegrees = 62.0;

    private const double CompactRingStrokeRatio = 0.18;
    private const double CompactCoreSideRatio = 0.27;

    private static readonly Brush White = Freeze(new SolidColorBrush(Colors.White));

    public static void Draw(
        DrawingContext dc,
        int size,
        PlatePalette palette = PlatePalette.Graphite,
        double gapAngleDegrees = GapAngleDegrees,
        double coreSideRatio = CoreSideRatio,
        double coreOffsetRatio = CoreOffsetRatio)
    {
        LogoA2.DrawPlate(dc, size, palette == PlatePalette.InkBlue);

        bool compact = size <= 20;
        double stroke = (compact ? CompactRingStrokeRatio : RingStrokeRatio) * S;
        double outerRadius = RingDiameterRatio * S / 2.0;
        double innerRadius = outerRadius - stroke;
        double cy = Center - RingCenterLiftRatio * S;

        double gapHalf = gapAngleDegrees / 2.0 * Math.PI / 180.0;
        double start = gapHalf;
        double end = -gapHalf + 2.0 * Math.PI;

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

        double core = (compact ? CompactCoreSideRatio : coreSideRatio) * S;
        double coreRadius = CoreRadiusRatio * S;
        double coreCx = Center + (compact ? CoreOffsetRatio : coreOffsetRatio) * S;
        var coreGeometry = new RectangleGeometry(
            new Rect(coreCx - core / 2.0, cy - core / 2.0, core, core), coreRadius, coreRadius);
        coreGeometry.Freeze();

        dc.DrawGeometry(White, null, coreGeometry);
    }

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}