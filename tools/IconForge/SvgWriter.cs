using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace Custodian.IconForge;

/// <summary>
/// Emits assets/custodian.svg from the same geometry objects the rasteriser
/// draws, so the vector source can not drift from the .ico (SvgCheck verifies
/// bounds and areas afterwards).
/// </summary>
internal static class SvgWriter
{
    public static string Build()
    {
        Geometry plate = LogoFinal.PlateGeometry();
        Geometry sand = LogoFinal.SandGeometry();
        Geometry body = LogoFinal.BodyGeometry();
        Geometry rim = LogoFinal.RimGeometry();
        Rect rimBounds = rim.Bounds;

        var svg = new StringBuilder();
        svg.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        svg.AppendLine("<!--");
        svg.AppendLine("  Custodian 应用图标（矢量源，1024x1024 设计坐标）");
        svg.AppendLine("  底板：squircle（|x|^4.6 + |y|^4.6 = 1，16 段三次贝塞尔），#2E8BF7 -> #0F4C9C 纵向渐变，");
        svg.AppendLine("        顶部 8% 白内高光；无描边、无外发光。");
        svg.AppendLine("  符号（沙盒家族）：白色盒身（上宽 0.56S、下宽 0.50S、高 0.225S 的圆角梯形）");
        svg.AppendLine("        + 白色上沿（宽 0.60S、厚 0.062S、圆角 0.025S，与盒身之间留 0.018S 底板蓝结构缝）");
        svg.AppendLine("        + 金沙（#F5C342，宽 0.50S，露出上沿 0.10S、下缘在缝里溢出 0.008S，顶边起拱 0.03S）");
        svg.AppendLine("  光栅由 tools/IconForge 用同一几何生成：generate / inspect / verify。");
        svg.AppendLine("-->");
        svg.AppendLine("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"1024\" height=\"1024\" viewBox=\"0 0 1024 1024\">");
        svg.AppendLine("  <title>Custodian</title>");
        svg.AppendLine("  <desc>蓝色底板上的白色沙盒，盒口盛着金沙，沙面上立着两枚应用小片</desc>");
        svg.AppendLine();
        svg.AppendLine("  <defs>");
        svg.AppendLine("    <linearGradient id=\"plateGradient\" x1=\"0\" y1=\"0\" x2=\"0\" y2=\"1\">");
        svg.AppendLine("      <stop offset=\"0\" stop-color=\"#2E8BF7\"/>");
        svg.AppendLine("      <stop offset=\"1\" stop-color=\"#0F4C9C\"/>");
        svg.AppendLine("    </linearGradient>");
        svg.AppendLine("    <linearGradient id=\"plateTopHighlight\" x1=\"0\" y1=\"0\" x2=\"0\" y2=\"1\">");
        svg.AppendLine("      <stop offset=\"0\" stop-color=\"#FFFFFF\" stop-opacity=\"0.08\"/>");
        svg.AppendLine("      <stop offset=\"0.3\" stop-color=\"#FFFFFF\" stop-opacity=\"0\"/>");
        svg.AppendLine("    </linearGradient>");
        svg.AppendLine();
        svg.AppendLine("    <path id=\"plateShape\" d=\"" + PathData(plate) + "\"/>");
        svg.AppendLine("    <path id=\"sandShape\" d=\"" + PathData(sand) + "\"/>");
        svg.AppendLine("    <path id=\"bodyShape\" d=\"" + PathData(body) + "\"/>");
        svg.AppendLine("  </defs>");
        svg.AppendLine();
        svg.AppendLine("  <g id=\"icon\">");
        svg.AppendLine("    <use href=\"#plateShape\" fill=\"url(#plateGradient)\"/>");
        svg.AppendLine("    <use href=\"#plateShape\" fill=\"url(#plateTopHighlight)\"/>");
        svg.AppendLine();
        svg.AppendLine("    <!-- 金沙：先画，下缘由盒身和上沿压住 -->");
        svg.AppendLine("    <use href=\"#sandShape\" fill=\"#F5C342\"/>");
        svg.AppendLine();
        svg.AppendLine("    <!-- 盒身 -->");
        svg.AppendLine("    <use href=\"#bodyShape\" fill=\"#FFFFFF\"/>");
        svg.AppendLine();
        svg.AppendLine("    <!-- 上沿 -->");
        svg.AppendLine($"    <rect id=\"rim\" x=\"{Num(rimBounds.X)}\" y=\"{Num(rimBounds.Y)}\" "
                    + $"width=\"{Num(rimBounds.Width)}\" height=\"{Num(rimBounds.Height)}\" "
                    + $"rx=\"{Num(LogoV3c.RimRadiusRatio * LogoV3c.S)}\" fill=\"#FFFFFF\"/>");
        svg.AppendLine("  </g>");
        svg.AppendLine("</svg>");
        return svg.ToString();
    }

    /// <summary>Serialises a geometry as SVG path data (Beziers and arcs stay native).</summary>
    private static string PathData(Geometry geometry)
    {
        var builder = new StringBuilder();
        PathGeometry path = PathGeometry.CreateFromGeometry(geometry);

        foreach (PathFigure figure in path.Figures)
        {
            builder.Append("M ").Append(Num(figure.StartPoint.X)).Append(' ').Append(Num(figure.StartPoint.Y));
            foreach (PathSegment segment in figure.Segments)
            {
                switch (segment)
                {
                    case LineSegment line:
                        builder.Append(" L ").Append(Num(line.Point.X)).Append(' ').Append(Num(line.Point.Y));
                        break;

                    case PolyLineSegment polyLine:
                        foreach (Point point in polyLine.Points)
                        {
                            builder.Append(" L ").Append(Num(point.X)).Append(' ').Append(Num(point.Y));
                        }

                        break;

                    case BezierSegment bezier:
                        builder.Append(" C ")
                            .Append(Num(bezier.Point1.X)).Append(' ').Append(Num(bezier.Point1.Y)).Append(' ')
                            .Append(Num(bezier.Point2.X)).Append(' ').Append(Num(bezier.Point2.Y)).Append(' ')
                            .Append(Num(bezier.Point3.X)).Append(' ').Append(Num(bezier.Point3.Y));
                        break;

                    case PolyBezierSegment polyBezier:
                        for (int i = 0; i + 2 < polyBezier.Points.Count; i += 3)
                        {
                            builder.Append(" C ")
                                .Append(Num(polyBezier.Points[i].X)).Append(' ').Append(Num(polyBezier.Points[i].Y)).Append(' ')
                                .Append(Num(polyBezier.Points[i + 1].X)).Append(' ').Append(Num(polyBezier.Points[i + 1].Y)).Append(' ')
                                .Append(Num(polyBezier.Points[i + 2].X)).Append(' ').Append(Num(polyBezier.Points[i + 2].Y));
                        }

                        break;

                    case QuadraticBezierSegment quadratic:
                        builder.Append(" Q ")
                            .Append(Num(quadratic.Point1.X)).Append(' ').Append(Num(quadratic.Point1.Y)).Append(' ')
                            .Append(Num(quadratic.Point2.X)).Append(' ').Append(Num(quadratic.Point2.Y));
                        break;

                    case PolyQuadraticBezierSegment polyQuadratic:
                        for (int i = 0; i + 1 < polyQuadratic.Points.Count; i += 2)
                        {
                            builder.Append(" Q ")
                                .Append(Num(polyQuadratic.Points[i].X)).Append(' ').Append(Num(polyQuadratic.Points[i].Y)).Append(' ')
                                .Append(Num(polyQuadratic.Points[i + 1].X)).Append(' ').Append(Num(polyQuadratic.Points[i + 1].Y));
                        }

                        break;

                    case ArcSegment arc:
                        builder.Append(" A ")
                            .Append(Num(arc.Size.Width)).Append(' ').Append(Num(arc.Size.Height)).Append(" 0 ")
                            .Append(arc.IsLargeArc ? '1' : '0').Append(' ')
                            .Append(arc.SweepDirection == SweepDirection.Clockwise ? '1' : '0').Append(' ')
                            .Append(Num(arc.Point.X)).Append(' ').Append(Num(arc.Point.Y));
                        break;

                    default:
                        throw new NotSupportedException($"不支持的路径段类型: {segment.GetType().Name}");
                }
            }

            if (figure.IsClosed)
            {
                builder.Append(" Z");
            }
        }

        return builder.ToString();
    }

    private static string Num(double value) =>
        Math.Round(value, 3).ToString("0.###", CultureInfo.InvariantCulture);
}