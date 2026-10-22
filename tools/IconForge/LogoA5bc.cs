using System.Windows;
using System.Windows.Media;

namespace Custodian.IconForge;

/// <summary>
/// A5b: hinged lid. The lid is welded to the box's top left corner (no floating
/// bar) and the box's top edge drops away to the right, so the graphite wedge
/// between lid and shoulder reads as an opening.
/// </summary>
internal static class LogoA5b
{
    private const double S = 1024.0;
    private const double Center = 512.0;

    private static readonly Brush White = Freeze(new SolidColorBrush(Colors.White));

    public static void Draw(DrawingContext dc, int size)
    {
        LogoA2.DrawPlate(dc, size);

        bool compact = size <= 20;
        double boxW = (compact ? 0.68 : 0.62) * S;
        double boxH = 0.44 * S;
        double boxR = 0.07 * S;
        double boxCy = 0.60 * S;
        double boxLeft = Center - boxW / 2.0;
        double boxRight = Center + boxW / 2.0;
        double boxTop = boxCy - boxH / 2.0;

        double drop = 0.06 * S;
        double flat = 0.30 * S;

        Geometry box = RoundRect(Center, boxCy, boxW, boxH, boxR);

        // Cut the right part of the top edge down by drop, keeping the left
        // flat stretch horizontal.
        var shoulder = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (StreamGeometryContext ctx = shoulder.Open())
        {
            ctx.BeginFigure(new Point(boxLeft + flat, boxTop), isFilled: true, isClosed: true);
            ctx.LineTo(new Point(boxRight + 4, boxTop + drop), isStroked: false, isSmoothJoin: false);
            ctx.LineTo(new Point(boxRight + 4, boxTop - 400), isStroked: false, isSmoothJoin: false);
            ctx.LineTo(new Point(boxLeft + flat, boxTop - 400), isStroked: false, isSmoothJoin: false);
        }

        shoulder.Freeze();
        box = Geometry.Combine(box, shoulder, GeometryCombineMode.Exclude, null);

        double lidT = (compact ? 0.145 : 0.115) * S;
        double lidLen = 0.50 * S;
        double lidR = 0.05 * S;
        double tilt = compact ? 9.0 : 14.0;
        double overlap = 0.03 * S;

        // Welded to the top left corner: the lid's bottom edge dips into the box
        // so both white shapes are one piece at the hinge.
        var lid = new RectangleGeometry(
            new Rect(boxLeft, boxTop - lidT + overlap, lidLen, lidT), lidR, lidR);
        lid.Transform = new RotateTransform(-tilt, boxLeft, boxTop);
        lid.Freeze();

        dc.DrawGeometry(White, null, box);
        dc.DrawGeometry(White, null, lid);
    }

    private static Geometry RoundRect(double cx, double cy, double sideX, double sideY, double radius)
    {
        var geometry = new RectangleGeometry(
            new Rect(cx - sideX / 2.0, cy - sideY / 2.0, sideX, sideY), radius, radius);
        geometry.Freeze();
        return geometry;
    }

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}

/// <summary>
/// A5c: one continuous silhouette. The box and the folded flap are the same white
/// mass - the flap rises from the right part of the top edge as a trapezoid whose
/// top edge is 12% shorter than its base, so the outline alone says "open flap".
/// </summary>
internal static class LogoA5c
{
    private const double S = 1024.0;
    private const double Center = 512.0;

    private static readonly Brush White = Freeze(new SolidColorBrush(Colors.White));

    public static void Draw(DrawingContext dc, int size)
    {
        LogoA2.DrawPlate(dc, size);

        bool compact = size <= 20;
        double boxW = 0.62 * S;
        double boxH = 0.44 * S;
        double boxR = 0.07 * S;
        double boxCy = 0.60 * S;
        double boxLeft = Center - boxW / 2.0;
        double boxRight = Center + boxW / 2.0;
        double boxTop = boxCy - boxH / 2.0;

        Geometry silhouette = RoundRect(Center, boxCy, boxW, boxH, boxR);

        double baseLeft = boxLeft + (compact ? 0.10 : 0.12) * S;
        double baseRight = boxRight;
        double baseLength = baseRight - baseLeft;
        double flapHeight = (compact ? 0.20 : 0.17) * S;
        double lean = 0.06 * S;
        double topLength = baseLength * 0.88;

        var bottomLeft = new Point(baseLeft, boxTop + 0.03 * S);
        var bottomRight = new Point(baseRight, boxTop + 0.03 * S);
        var topLeft = new Point(baseLeft + lean, boxTop - flapHeight + 0.02 * S);
        var topRight = new Point(baseLeft + lean + topLength, boxTop - flapHeight + 0.02 * S);

        var flap = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (StreamGeometryContext ctx = flap.Open())
        {
            ctx.BeginFigure(bottomLeft, isFilled: true, isClosed: true);
            ctx.LineTo(bottomRight, isStroked: true, isSmoothJoin: false);
            ctx.LineTo(topRight, isStroked: true, isSmoothJoin: false);
            ctx.LineTo(topLeft, isStroked: true, isSmoothJoin: false);
        }

        flap.Freeze();

        var rounded = new GeometryGroup { FillRule = FillRule.Nonzero };
        rounded.Children.Add(flap);
        rounded.Children.Add(new EllipseGeometry(topLeft, 0.03 * S, 0.03 * S));
        rounded.Children.Add(new EllipseGeometry(topRight, 0.03 * S, 0.03 * S));
        rounded.Freeze();

        silhouette = Geometry.Combine(silhouette, rounded, GeometryCombineMode.Union, null);

        dc.DrawGeometry(White, null, silhouette);
    }

    private static Geometry RoundRect(double cx, double cy, double sideX, double sideY, double radius)
    {
        var geometry = new RectangleGeometry(
            new Rect(cx - sideX / 2.0, cy - sideY / 2.0, sideX, sideY), radius, radius);
        geometry.Freeze();
        return geometry;
    }

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
