using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;

namespace Custodian.IconForge;

/// <summary>
/// Checks that assets/custodian.svg uses exactly the geometry the rasteriser
/// draws (same plate, sand, body, rim and tiles), so the vector source and the
/// .ico cannot drift apart unnoticed.
/// </summary>
internal static class SvgCheck
{
    public static int Run(string svgPath, TextWriter output)
    {
        if (!File.Exists(svgPath))
        {
            output.WriteLine($"[verify] 找不到 {svgPath}");
            return 2;
        }

        string text = File.ReadAllText(svgPath);
        var problems = new List<string>();

        try
        {
            System.Xml.Linq.XDocument.Parse(text);
            output.WriteLine("[verify] SVG 是合法 XML。");
        }
        catch (System.Xml.XmlException ex)
        {
            problems.Add($"SVG 不是合法 XML: {ex.Message}");
        }

        Geometry? plate = ReadPath(text, "plateShape", problems);
        Geometry? sand = ReadPath(text, "sandShape", problems);
        Geometry? body = ReadPath(text, "bodyShape", problems);

        if (plate is not null)
        {
            CheckGeometry("plateShape", plate, LogoFinal.PlateGeometry(), problems, output);
        }

        if (sand is not null)
        {
            CheckGeometry("sandShape ", sand, LogoFinal.SandGeometry(), problems, output);
        }

        if (body is not null)
        {
            CheckGeometry("bodyShape ", body, LogoFinal.BodyGeometry(), problems, output);
        }

        CheckRect(text, "rim", LogoFinal.RimGeometry().Bounds, LogoV3c.RimRadiusRatio * LogoV3c.S, problems, output);

        if (problems.Count == 0)
        {
            output.WriteLine("[verify] SVG 与光栅几何一致。");
            return 0;
        }

        output.WriteLine($"[verify] 发现 {problems.Count} 处不一致:");
        foreach (string problem in problems)
        {
            output.WriteLine($"  - {problem}");
        }

        return 1;
    }

    private static void CheckGeometry(
        string name, Geometry fromSvg, Geometry fromCode, List<string> problems, TextWriter output)
    {
        Rect a = fromSvg.Bounds;
        Rect b = fromCode.Bounds;
        double areaA = fromSvg.GetArea();
        double areaB = fromCode.GetArea();
        double areaError = Math.Abs(areaA - areaB) / Math.Max(1.0, areaB);

        output.WriteLine($"[verify]   {name} bounds svg=({a.X:F2},{a.Y:F2},{a.Width:F2},{a.Height:F2}) "
                       + $"code=({b.X:F2},{b.Y:F2},{b.Width:F2},{b.Height:F2}) "
                       + $"area svg={areaA:F1} code={areaB:F1}");

        if (Math.Abs(a.X - b.X) > 0.05 || Math.Abs(a.Y - b.Y) > 0.05
            || Math.Abs(a.Width - b.Width) > 0.05 || Math.Abs(a.Height - b.Height) > 0.05)
        {
            problems.Add($"{name.Trim()} 包围盒与代码生成的不一致");
        }

        if (areaError > 0.001)
        {
            problems.Add($"{name.Trim()} 面积偏差 {areaError:P3}");
        }
    }

    private static void CheckRect(
        string text, string id, Rect expected, double expectedRadius, List<string> problems, TextWriter output)
    {
        Match match = Regex.Match(
            text,
            $"<rect\\s+id=\"{Regex.Escape(id)}\"\\s+x=\"([-0-9.]+)\"\\s+y=\"([-0-9.]+)\""
            + "\\s+width=\"([-0-9.]+)\"\\s+height=\"([-0-9.]+)\"\\s+rx=\"([-0-9.]+)\"");

        if (!match.Success)
        {
            problems.Add($"SVG 里找不到 id={id} 的 rect");
            return;
        }

        double x = Parse(match.Groups[1].Value);
        double y = Parse(match.Groups[2].Value);
        double width = Parse(match.Groups[3].Value);
        double height = Parse(match.Groups[4].Value);
        double radius = Parse(match.Groups[5].Value);

        output.WriteLine($"[verify]   {id,-8} svg=({x:F1},{y:F1},{width:F1}x{height:F1},r={radius:F1}) "
                       + $"code=({expected.X:F1},{expected.Y:F1},{expected.Width:F1}x{expected.Height:F1},r={expectedRadius:F1})");

        if (Math.Abs(x - expected.X) > 0.05 || Math.Abs(y - expected.Y) > 0.05
            || Math.Abs(width - expected.Width) > 0.05 || Math.Abs(height - expected.Height) > 0.05
            || Math.Abs(radius - expectedRadius) > 0.05)
        {
            problems.Add($"{id} 的尺寸/位置与代码生成的不一致");
        }
    }

    private static Geometry? ReadPath(string text, string id, List<string> problems)
    {
        Match match = Regex.Match(text, $"<path\\s+id=\"{Regex.Escape(id)}\"\\s+d=\"([^\"]+)\"");
        if (!match.Success)
        {
            problems.Add($"SVG 里找不到 id={id} 的 path");
            return null;
        }

        try
        {
            return Geometry.Parse(match.Groups[1].Value);
        }
        catch (FormatException ex)
        {
            problems.Add($"path {id} 解析失败: {ex.Message}");
            return null;
        }
    }

    private static double Parse(string value) =>
        double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
}