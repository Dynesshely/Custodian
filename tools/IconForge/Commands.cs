using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Custodian.IconForge;

internal static class Commands
{
    private static readonly int[] BmpSizes = { 16, 20, 24, 32, 40, 48, 64 };
    private static readonly int[] PngSizes = { 128, 256, 512 };

    public static int Help()
    {
        Console.WriteLine("IconForge - Custodian 应用图标生成器（离线，无 NuGet 依赖）");
        Console.WriteLine();
        Console.WriteLine("用法:");
        Console.WriteLine("  IconForge generate [--out <目录>] [--repo-root <目录>]");
        Console.WriteLine("      渲染 assets/custodian.ico (16 20 24 32 40 48 64 BMP + 128 256 PNG)、");
        Console.WriteLine("      assets/custodian-256.png、assets/custodian-1024.png 并自检。");
        Console.WriteLine("  IconForge inspect [<path.ico>]");
        Console.WriteLine("      打印 ICO 条目表并做结构校验（不指定路径时用 assets/custodian.ico）。");
        Console.WriteLine("  IconForge preview [--out <目录>]");
        Console.WriteLine("      生成各尺寸预览、最近邻放大图、深浅任务栏模拟图（默认写到 %TEMP%）。");
        Console.WriteLine("  IconForge verify [<custodian.svg>]");
        Console.WriteLine("      校验 SVG 的 path 与光栅使用的几何完全一致。");
        return 0;
    }

    public static int Unknown(string command)
    {
        Console.Error.WriteLine($"未知命令: {command}");
        Environment.ExitCode = 2;
        Help();
        return 2;
    }

    public static int Generate(CliOptions options)
    {
        string repoRoot = Paths.RepoRoot(options.Get("repo-root"));
        string outDir = options.Get("out") is { Length: > 0 } custom
            ? Path.GetFullPath(custom)
            : Path.Combine(repoRoot, "assets");
        Directory.CreateDirectory(outDir);

        Console.WriteLine($"[generate] 仓库根 : {repoRoot}");
        Console.WriteLine($"[generate] 输出目录: {outDir}");
        Console.WriteLine();

        var entries = new List<IcoFormat.Entry>();
        var rendered = new Dictionary<int, BitmapSource>();

        foreach (int size in BmpSizes.Concat(PngSizes))
        {
            BitmapSource image = IconRender.Render(size);
            rendered[size] = image;

            bool isPng = IconRender.IsPngEntry(size);
            byte[] payload = isPng
                ? IconRender.EncodePng(image)
                : IcoFormat.BuildBmpEntry(image, size);

            entries.Add(new IcoFormat.Entry(size, isPng, payload));
            Console.WriteLine($"  {size,3} px  {(isPng ? "PNG" : "BMP"),-3}  {payload.Length,8:N0} B");
        }

        byte[] ico = IcoFormat.Build(entries);
        string icoPath = Path.Combine(outDir, "custodian.ico");
        File.WriteAllBytes(icoPath, ico);

        string png256 = Path.Combine(outDir, "custodian-256.png");
        string png1024 = Path.Combine(outDir, "custodian-1024.png");
        IconRender.SavePng(rendered[256], png256);
        IconRender.SavePng(IconRender.Render(1024), png1024);

        string svgPath = Path.Combine(outDir, "custodian.svg");
        File.WriteAllText(svgPath, SvgWriter.Build(), new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        Console.WriteLine();
        Console.WriteLine($"  写入 {Path.GetFileName(icoPath)}        {ico.Length,8:N0} B");
        Console.WriteLine($"  写入 {Path.GetFileName(png256)}  {new FileInfo(png256).Length,8:N0} B");
        Console.WriteLine($"  写入 {Path.GetFileName(png1024)} {new FileInfo(png1024).Length,8:N0} B");
        Console.WriteLine($"  写入 {Path.GetFileName(svgPath)}  {new FileInfo(svgPath).Length,8:N0} B");
        Console.WriteLine();
        Console.WriteLine("[generate] 自检 ICO 结构:");
        Console.WriteLine();

        int result = IcoFormat.Inspect(icoPath, Console.Out);

        if (File.Exists(svgPath))
        {
            Console.WriteLine();
            int svgResult = SvgCheck.Run(svgPath, Console.Out);
            result = Math.Max(result, svgResult);
        }

        return result;
    }

    public static int Inspect(CliOptions options)
    {
        string? explicitPath = options.Positional.Count > 0 ? options.Positional[0] : options.Get("path");
        string path = explicitPath is { Length: > 0 }
            ? Path.GetFullPath(explicitPath)
            : Path.Combine(Paths.RepoRoot(options.Get("repo-root")), "assets", "custodian.ico");

        return IcoFormat.Inspect(path, Console.Out);
    }

    public static int Verify(CliOptions options)
    {
        string? explicitPath = options.Positional.Count > 0 ? options.Positional[0] : options.Get("path");
        string path = explicitPath is { Length: > 0 }
            ? Path.GetFullPath(explicitPath)
            : Path.Combine(Paths.RepoRoot(options.Get("repo-root")), "assets", "custodian.svg");

        return SvgCheck.Run(path, Console.Out);
    }

    public static int Preview(CliOptions options)
    {
        string outDir = options.Get("out") is { Length: > 0 } custom
            ? Path.GetFullPath(custom)
            : Path.Combine(Path.GetTempPath(), "custodian-preview");
        Directory.CreateDirectory(outDir);

        int supersample = int.TryParse(options.Get("ss"), out int parsed) ? parsed : -1;
        int[] sizes = { 1024, 256, 128, 64, 48, 32, 24, 20, 16 };
        var cache = new Dictionary<int, BitmapSource>();

        Console.WriteLine($"[preview] 输出目录: {outDir} (supersample={(supersample < 0 ? "auto" : supersample.ToString())})");
        foreach (int size in sizes)
        {
            BitmapSource image = IconRender.Render(size, supersample);
            cache[size] = image;
            IconRender.SavePng(image, Path.Combine(outDir, $"custodian-{size}.png"));
        }

        (int Size, int Factor)[] zooms = { (16, 8), (20, 8), (24, 6), (32, 4), (48, 3), (64, 2) };
        foreach ((int size, int factor) in zooms)
        {
            IconRender.SavePng(
                IconRender.ZoomNearest(cache[size], factor),
                Path.Combine(outDir, $"zoom-{size}-{factor}x.png"));
        }

        WriteStrip(Path.Combine(outDir, "taskbar-light.png"), Color.FromRgb(0xF3, 0xF3, 0xF3), cache);
        WriteStrip(Path.Combine(outDir, "taskbar-dark.png"), Color.FromRgb(0x1E, 0x1E, 0x1E), cache);
        WriteStrip(Path.Combine(outDir, "wallpaper-mid.png"), Color.FromRgb(0x8A, 0x8F, 0x9A), cache);

        // Decode the generated .ico through the WPF shell decoder: proves Windows
        // itself reads every frame (32 bpp BMP entries and PNG entries alike).
        string icoPath = Path.Combine(Paths.RepoRoot(options.Get("repo-root")), "assets", "custodian.ico");
        if (File.Exists(icoPath))
        {
            var decoder = new IconBitmapDecoder(new Uri(icoPath), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            Console.WriteLine($"[preview] ico 帧解码: {decoder.Frames.Count} 帧 <- {icoPath}");
            for (int i = 0; i < decoder.Frames.Count; i++)
            {
                BitmapSource frame = decoder.Frames[i];
                Console.WriteLine($"  #{i} {frame.PixelWidth}x{frame.PixelHeight} {frame.Format}");
                IconRender.SavePng(frame, Path.Combine(outDir, $"ico-frame-{i}-{frame.PixelWidth}.png"));
            }
        }

        foreach (string file in Directory.GetFiles(outDir, "*.png").OrderBy(f => f))
        {
            Console.WriteLine($"  {Path.GetFileName(file),-24} {new FileInfo(file).Length,9:N0} B");
        }

        Console.WriteLine();
        Console.WriteLine("[preview] 用 read_image 检查 zoom-16-8x.png（16px 放大 8 倍）、custodian-256.png、");
        Console.WriteLine("          taskbar-light.png / taskbar-dark.png（深浅任务栏共存）。");
        return 0;
    }

    /// <summary>Horizontal strip that mimics a taskbar row at 100% scale.</summary>
    private static void WriteStrip(string path, Color background, IReadOnlyDictionary<int, BitmapSource> cache)
    {
        (int Size, double Gap)[] row = { (32, 36), (24, 30), (20, 26), (16, 24), (48, 36) };
        const double padding = 24;
        double height = 64;
        double width = padding * 2 + row.Sum(item => item.Size + item.Gap);

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(background), null, new Rect(0, 0, width, height));
            double x = padding;
            foreach ((int size, double gap) in row)
            {
                double y = (height - size) / 2.0;
                dc.DrawImage(cache[size], new Rect(x, y, size, size));
                x += size + gap;
            }
        }

        var target = new RenderTargetBitmap(
            (int)Math.Ceiling(width), (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        IconRender.SavePng(target, path);
    }
}

/// <summary>Locates the repository root so the tool can be run from anywhere.</summary>
internal static class Paths
{
    public static string RepoRoot(string? overrideRoot)
    {
        if (overrideRoot is { Length: > 0 })
        {
            return Path.GetFullPath(overrideRoot);
        }

        // 1) walk up from the current directory, 2) fall back to the executable
        // location so the tool also works when started from somewhere else.
        return WalkUp(Directory.GetCurrentDirectory())
            ?? WalkUp(AppContext.BaseDirectory)
            ?? Directory.GetCurrentDirectory();
    }

    private static string? WalkUp(string start)
    {
        var directory = new DirectoryInfo(start);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Custodian.slnx"))
                || File.Exists(Path.Combine(directory.FullName, "assets", "custodian.svg")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}