using System.Windows;
using System.Windows.Media;

namespace Custodian.IconForge;

/// <summary>
/// Vector model of the Custodian mark, expressed in a 1024 x 1024 design space.
///
/// Plate  : superellipse (squircle) filled with a blue/violet gradient.
/// Symbol : a white open ring (reads as "C" - the custodian) with a small
///          rounded square held inside it (the sandboxed app).
///
/// The same numbers are mirrored by assets/custodian.svg so that the raster
/// layers and the vector source share one geometry.
/// </summary>
internal static class IconDesign
{
    /// <summary>Side of the square design canvas.</summary>
    public const double DesignSize = 1024.0;

    private const double DesignCenter = DesignSize / 2.0;

    // ---- plate -------------------------------------------------------------
    // Half extent of the squircle; the plate spans 56..968 on both axes.
    public const double PlateHalf = 456.0;

    // |x|^n + |y|^n = 1; n = 4.6 gives the Windows 11 style rounded square
    // (not a true circle, not a plain rounded rectangle).
    private const double PlateExponent = 4.6;

    // 16 cubic Bezier segments (22.5 degrees each) approximate the
    // superellipse to within 0.04 design units (< 0.004% of the canvas).
    private const int PlateSegmentCount = 16;

    // Handle length of each segment as a fraction of its chord, measured
    // numerically against the true superellipse. Indexed by segment % 4.
    private static readonly (double Start, double End)[] SegmentHandles =
    {
        (0.510, 0.225),
        (0.370, 0.325),
        (0.325, 0.370),
        (0.225, 0.510),
    };

    // ---- symbol ------------------------------------------------------------
    // Open ring: the 66 degree opening faces east so the glyph reads as "C".
    // Centre sits exactly on 512 so the ring centre lands on a whole pixel at
    // 16/20/24/32/40/48/64 px (8, 10, 12, 16, 20, 24, 32).
    public const double RingCenterX = 512.0;
    public const double RingCenterY = 512.0;
    public const double RingOuterRadius = 300.0;
    public const double RingInnerRadius = 190.0;
    public const double RingGapHalfAngle = 33.0;

    // Rounded square suspended inside the ring.
    public const double CoreSide = 163.0;
    public const double CoreCornerRadius = 32.0;

    // ---- palette -----------------------------------------------------------
    private static readonly Color GradientStart = Color.FromRgb(0x6A, 0x74, 0xFF);
    private static readonly Color GradientMiddle = Color.FromRgb(0x3C, 0x47, 0xDC);
    private static readonly Color GradientEnd = Color.FromRgb(0x1D, 0x24, 0x72);

    private static readonly Brush PlateBaseBrush = CreatePlateBaseBrush();
    private static readonly Brush TopGlowBrush = CreateVerticalBrush(
        (0.00, Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
        (0.46, Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF)));

    private static readonly Brush BottomShadeBrush = CreateVerticalBrush(
        (0.52, Color.FromArgb(0x00, 0x00, 0x00, 0x00)),
        (1.00, Color.FromArgb(0x22, 0x00, 0x00, 0x00)));

    private static readonly Brush SheenBrush = CreateSheenBrush();
    private static readonly Pen RimPen = CreateRimPen();
    private static readonly Brush SymbolBrush = CreateSymbolBrush();

    /// <summary>Draws the whole mark in design coordinates (0..1024).</summary>
    /// <param name="targetPixelSize">Final raster size; used for optical scaling only.</param>
    public static void Draw(DrawingContext dc, int targetPixelSize)
    {
        double optical = OpticalScale(targetPixelSize);
        double half = PlateHalfFor(targetPixelSize);
        Geometry plate = CreatePlateGeometry(half);

        // Plate: base gradient, then the lighting layers. Below 32 px the
        // highlight and sheen only cost contrast against the white symbol, so
        // they are dropped (only the bottom shade stays, it helps the symbol).
        dc.DrawGeometry(PlateBaseBrush, null, plate);
        if (targetPixelSize >= 32)
        {
            dc.DrawGeometry(TopGlowBrush, null, plate);
            dc.DrawGeometry(SheenBrush, null, plate);
        }

        dc.DrawGeometry(BottomShadeBrush, null, plate);

        // Hairline inner light along the edge (only reads at >= 128 px).
        if (targetPixelSize >= 64)
        {
            dc.DrawGeometry(null, RimPen, CreatePlateGeometry(half - 5.0));
        }

        // Symbol.
        dc.DrawGeometry(SymbolBrush, null, CreateRingGeometry(optical));
        dc.DrawGeometry(SymbolBrush, null, CreateCoreGeometry(optical));
    }

    /// <summary>Squircle outline built from 16 cubic segments of a superellipse.</summary>
    public static Geometry CreatePlateGeometry(double half = PlateHalf)
    {
        var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(PlatePoint(0.0, half), isFilled: true, isClosed: true);
            foreach ((Point _, Point c1, Point c2, Point p3) in PlateSegments(half))
            {
                ctx.BezierTo(c1, c2, p3, isStroked: true, isSmoothJoin: false);
            }
        }

        geometry.Freeze();
        return geometry;
    }

    /// <summary>The same 16 cubic segments as raw points, so the SVG source can mirror them.</summary>
    public static (Point Start, Point Control1, Point Control2, Point End)[] PlateSegments(double half)
    {
        const double step = 360.0 / PlateSegmentCount;
        var segments = new (Point, Point, Point, Point)[PlateSegmentCount];

        for (int i = 0; i < PlateSegmentCount; i++)
        {
            double a0 = i * step;
            double a1 = a0 + step;
            (double handleStart, double handleEnd) = SegmentHandles[i % 4];

            Point p0 = PlatePoint(a0, half);
            Point p3 = PlatePoint(a1, half);
            double chord = (p3 - p0).Length;

            Point c1 = p0 + PlateTangent(a0) * (handleStart * chord);
            Point c2 = p3 - PlateTangent(a1) * (handleEnd * chord);

            segments[i] = (p0, c1, c2, p3);
        }

        return segments;
    }

    /// <summary>Open ring (annulus with a wedge removed) - the "C" glyph.</summary>
    public static Geometry CreateRingGeometry(double scale = 1.0)
    {
        double cx = DesignCenter + (RingCenterX - DesignCenter) * scale;
        double cy = DesignCenter + (RingCenterY - DesignCenter) * scale;
        double outer = RingOuterRadius * scale;
        double inner = RingInnerRadius * scale;
        double gap = RingGapHalfAngle * Math.PI / 180.0;

        Point OuterAt(double angle) => new(cx + outer * Math.Cos(angle), cy + outer * Math.Sin(angle));
        Point InnerAt(double angle) => new(cx + inner * Math.Cos(angle), cy + inner * Math.Sin(angle));

        var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(OuterAt(gap), isFilled: true, isClosed: true);
            ctx.ArcTo(OuterAt(-gap), new Size(outer, outer), 0.0, isLargeArc: true,
                SweepDirection.Clockwise, isStroked: true, isSmoothJoin: false);
            ctx.LineTo(InnerAt(-gap), isStroked: true, isSmoothJoin: false);
            ctx.ArcTo(InnerAt(gap), new Size(inner, inner), 0.0, isLargeArc: true,
                SweepDirection.Counterclockwise, isStroked: true, isSmoothJoin: false);
        }

        geometry.Freeze();
        return geometry;
    }

    /// <summary>Small rounded square inside the ring.</summary>
    public static Geometry CreateCoreGeometry(double scale = 1.0)
    {
        double cx = DesignCenter + (RingCenterX - DesignCenter) * scale;
        double cy = DesignCenter + (RingCenterY - DesignCenter) * scale;
        double side = CoreSide * scale;
        double radius = CoreCornerRadius * scale;

        var geometry = new RectangleGeometry(
            new Rect(cx - side / 2.0, cy - side / 2.0, side, side), radius, radius);
        geometry.Freeze();
        return geometry;
    }

    /// <summary>
    /// Tiny sizes get a slightly larger symbol so the ring stroke stays above
    /// one and a half pixels after antialiasing.
    /// </summary>
    private static double OpticalScale(int pixelSize) => pixelSize switch
    {
        <= 16 => 1.180,   // ring: 11.1 px across, 2.0 px stroke, 3.0 px core
        <= 20 => 1.090,
        <= 24 => 1.045,
        _ => 1.0,
    };

    /// <summary>
    /// The plate bleeds closer to the canvas edge at tiny sizes so the icon
    /// keeps a usable footprint in a 16 px taskbar slot.
    /// </summary>
    private static double PlateHalfFor(int pixelSize) => pixelSize switch
    {
        <= 16 => 512.0,   // full bleed: plate edges land on the pixel grid
        <= 20 => 500.0,
        <= 24 => 484.0,
        _ => PlateHalf,
    };

    private static Point PlatePoint(double angleDegrees, double half)
    {
        double angle = angleDegrees * Math.PI / 180.0;
        double cos = Math.Cos(angle);
        double sin = Math.Sin(angle);
        double x = System.Math.Sign(cos) * Math.Pow(Math.Abs(cos), 2.0 / PlateExponent);
        double y = System.Math.Sign(sin) * Math.Pow(Math.Abs(sin), 2.0 / PlateExponent);
        return new Point(DesignCenter + half * x, DesignCenter + half * y);
    }

    private static Vector PlateTangent(double angleDegrees)
    {
        const double delta = 0.0001;
        Vector direction = PlatePoint(angleDegrees + delta, PlateHalf) - PlatePoint(angleDegrees - delta, PlateHalf);
        direction.Normalize();
        return direction;
    }

    private static Brush CreatePlateBaseBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0.14, 0.00),
            EndPoint = new Point(0.86, 1.00),
        };
        brush.GradientStops.Add(new GradientStop(GradientStart, 0.00));
        brush.GradientStops.Add(new GradientStop(GradientMiddle, 0.46));
        brush.GradientStops.Add(new GradientStop(GradientEnd, 1.00));
        brush.Freeze();
        return brush;
    }

    private static Brush CreateVerticalBrush(params (double Offset, Color Color)[] stops)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0.0, 0.0),
            EndPoint = new Point(0.0, 1.0),
        };
        foreach ((double offset, Color color) in stops)
        {
            brush.GradientStops.Add(new GradientStop(color, offset));
        }

        brush.Freeze();
        return brush;
    }

    private static Brush CreateSheenBrush()
    {
        var brush = new RadialGradientBrush
        {
            Center = new Point(0.28, 0.16),
            GradientOrigin = new Point(0.26, 0.12),
            RadiusX = 0.86,
            RadiusY = 0.86,
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF), 0.00));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1.00));
        brush.Freeze();
        return brush;
    }

    private static Pen CreateRimPen()
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF)), 5.0);
        pen.Freeze();
        return pen;
    }

    private static Brush CreateSymbolBrush()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
        brush.Freeze();
        return brush;
    }
}