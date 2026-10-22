using System.Windows;
using System.Windows.Media;

namespace Custodian.IconForge;

/// <summary>
/// The approved final mark (v3c sandbox family). Everything that writes assets/
/// goes through here so the raster and the SVG share one geometry source.
/// </summary>
internal static class LogoFinal
{
    public static void Draw(DrawingContext dc, int size) => LogoV3c.Draw(dc, size);

    public static Geometry PlateGeometry() => LogoV3c.CreatePlateGeometry();

    public static Geometry SandGeometry() => LogoV3c.CreateSandGeometry();

    public static Geometry BodyGeometry() => LogoV3c.CreateBodyGeometry();

    public static Geometry RimGeometry() => LogoV3c.CreateRimGeometry();
}