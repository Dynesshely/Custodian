using System.Windows;
using System.Windows.Media;

namespace Custodian.IconForge;

/// <summary>
/// Direction A5: a solid white box with a solid lid tilted open. No outlines -
/// the box is a filled rounded rectangle with a small rounded hole showing the
/// graphite through, and the lid is a filled bar pivoting on the box's top left
/// corner. The graphite gap between lid and box is negative space, never a line.
/// </summary>
internal static class LogoA5
{
    private const double S = 1024.0;
    private const double Center = 512.0;

    private const double BoxWidthRatio = 0.60;
    private const double BoxHeightRatio = 0.42;
    private const double BoxRadiusRatio = 0.07;
    private const double BoxCenterYRatio = 0.575;

    private const double LidLengthRatio = 0.56;
    private const double LidThicknessRatio = 0.115;
    private const double LidRadiusRatio = 0.048;
    private const double LidTiltDegrees = 16.0;
    private const double LidGapRatio = 0.035;

    private const double HoleSideRatio = 0.13;
    private const double HoleRadiusRatio = 0.03;
    private const double HoleOffsetRatio = 0.02;

    // 16 / 20 px: wider box, flatter lid, thicker lid and a gap that survives AA.
    private const double CompactBoxWidthRatio = 0.66;
    private const double CompactLidTiltDegrees = 10.0;
    private const double CompactLidThicknessRatio = 0.14;
    private const double CompactLidGapRatio = 0.05;

    private static readonly Brush White = Freeze(new SolidColorBrush(Colors.White));

    public static void Draw(DrawingContext dc, int size)
    {
        LogoA2.DrawPlate(dc, size);
        DrawGlyph(dc, size);
    }

    public static void DrawGlyph(DrawingContext dc, int size)
    {
        bool compact = size <= 20;

        double boxWidth = (compact ? CompactBoxWidthRatio : BoxWidthRatio) * S;
        double boxHeight = BoxHeightRatio * S;
        double boxRadius = BoxRadiusRatio * S;
        double boxCenterY = BoxCenterYRatio * S;
        double boxLeft = Center - boxWidth / 2.0;
        double boxTop = boxCenterY - boxHeight / 2.0;

        double lidLength = LidLengthRatio * S;
        double lidThickness = (compact ? CompactLidThicknessRatio : LidThicknessRatio) * S;
        double lidRadius = LidRadiusRatio * S;
        double tilt = compact ? CompactLidTiltDegrees : LidTiltDegrees;
        double gap = compact
            ? Math.Max(CompactLidGapRatio * S, 1.5 * S / size)
            : Math.Max(LidGapRatio * S, 1.0 * S / size);

        Geometry box = RoundRect(Center, boxCenterY, boxWidth, boxHeight, boxRadius);

        if (size >= 32)
        {
            double hole = HoleSideRatio * S;
            Geometry holeGeometry = RoundRect(
                Center, boxCenterY + HoleOffsetRatio * S, hole, hole, HoleRadiusRatio * S);
            box = Geometry.Combine(box, holeGeometry, GeometryCombineMode.Exclude, null);
        }

        var lid = new RectangleGeometry(
            new Rect(boxLeft, boxTop - gap - lidThickness, lidLength, lidThickness), lidRadius, lidRadius);
        lid.Transform = new RotateTransform(-tilt, boxLeft, boxTop);
        lid.Freeze();

        Transform transform = compact
            ? CreateFitTransform(box, lid, boxWidth, boxHeight, boxCenterY, lidLength, lidThickness)
            : Transform.Identity;

        dc.PushTransform(transform);
        dc.DrawGeometry(White, null, box);
        dc.DrawGeometry(White, null, lid);
        dc.Pop();
    }

    /// <summary>
    /// Small sizes keep the proportions but are scaled to fit and re-centred, so a
    /// 16 px slot still reads as "a block with a raised lip" instead of a blob.
    /// </summary>
    private static Transform CreateFitTransform(
        Geometry box,
        Geometry lid,
        double boxWidth,
        double boxHeight,
        double boxCenterY,
        double lidLength,
        double lidThickness)
    {
        Rect bounds = Rect.Union(box.Bounds, lid.Bounds);
        double longest = Math.Max(bounds.Width, bounds.Height);
        double limit = 0.86 * S;
        double scale = longest > limit ? limit / longest : 1.0;

        // Centre by mass (box dominates), not by bounding box: the lid pulls the
        // bounding box up, which would otherwise leave the mark sitting low.
        Rect lidBounds = lid.Bounds;
        double boxArea = boxWidth * boxHeight;
        double lidArea = lidLength * lidThickness;
        double centerX = (boxArea * Center + lidArea * (lidBounds.X + lidBounds.Width / 2.0)) / (boxArea + lidArea);
        double centerY = (boxArea * boxCenterY + lidArea * (lidBounds.Y + lidBounds.Height / 2.0)) / (boxArea + lidArea);

        var transform = new TransformGroup();
        transform.Children.Add(new TranslateTransform(-centerX, -centerY));
        transform.Children.Add(new ScaleTransform(scale, scale));
        transform.Children.Add(new TranslateTransform(Center, Center));
        transform.Freeze();
        return transform;
    }

    private static Geometry RoundRect(double cx, double cy, double sideX, double sideY, double radius)
    {
        var geometry = new RectangleGeometry(
            new Rect(cx - sideX / 2.0, cy - sideY / 2.0, sideX, sideY),
            Math.Max(0.0, radius),
            Math.Max(0.0, radius));
        geometry.Freeze();
        return geometry;
    }

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}