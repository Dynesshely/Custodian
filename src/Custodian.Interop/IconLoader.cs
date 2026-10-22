using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Custodian.Interop.Native;

namespace Custodian.Interop;

/// <summary>
/// Extracts high quality icons from files, shortcuts and shell namespace items.
/// The preferred source is IShellItemImageFactory (ICONONLY + BIGGERSIZEOK), then the icon
/// resource itself (SHDefExtractIcon), then the shell's large icon.
/// </summary>
public static class IconLoader
{
    private const int DefaultSize = 48;
    private const int MaximumSize = 256;

    public static ImageSource? Load(string path, int size = DefaultSize)
    {
        if (size <= 0)
        {
            size = DefaultSize;
        }

        if (size > MaximumSize)
        {
            size = MaximumSize;
        }

        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            var location = Expand(path);
            if (location.Length == 0)
            {
                return null;
            }

            if (location.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                return LoadShortcut(location, size);
            }

            return LoadFile(location, size, 0);
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource? LoadShortcut(string lnkPath, int size)
    {
        // 1. Honour the icon location stored in the link (may point to an icon resource index).
        if (ShortcutResolver.TryGetIconLocation(lnkPath, out var iconLocation, out var iconIndex)
            && !string.IsNullOrWhiteSpace(iconLocation))
        {
            var iconSourcePath = Expand(iconLocation);
            if (iconSourcePath.Length > 0 && File.Exists(iconSourcePath))
            {
                var fromIconLocation = LoadFile(iconSourcePath, size, iconIndex, preferIconIndex: iconIndex != 0);
                if (fromIconLocation is not null)
                {
                    return fromIconLocation;
                }
            }
        }

        // 2. The target executable: the crispest source when the link is intact.
        var target = ShortcutResolver.Resolve(lnkPath)?.TargetPath;
        if (!string.IsNullOrWhiteSpace(target))
        {
            var targetPath = Expand(target);
            if (targetPath.Length > 0 && File.Exists(targetPath))
            {
                var fromTarget = LoadFile(targetPath, size, 0);
                if (fromTarget is not null)
                {
                    return fromTarget;
                }
            }
        }

        // 3. Target missing (common for links inside a sandbox): let the shell render the .lnk itself.
        return LoadFile(lnkPath, size, 0);
    }

    private static ImageSource? LoadFile(string path, int size, int iconIndex, bool preferIconIndex = false)
    {
        if (preferIconIndex)
        {
            var indexed = LoadWithIconIndex(path, size, iconIndex);
            if (indexed is not null)
            {
                return indexed;
            }
        }

        var image = LoadWithImageFactory(path, size);
        if (image is not null)
        {
            return Fit(image, size);
        }

        var extracted = LoadWithIconIndex(path, size, iconIndex);
        if (extracted is not null)
        {
            return extracted;
        }

        return LoadWithShellInfo(path, size);
    }

    private static BitmapSource? LoadWithImageFactory(string path, int size)
    {
        IShellItemImageFactory? factory = null;
        try
        {
            var iid = NativeMethods.IID_IShellItemImageFactory;
            var exists = File.Exists(path) || Directory.Exists(path);
            if (!exists)
            {
                // Shell APIs cannot image a path that does not exist; skip straight to attribute based fallback.
                return null;
            }

            NativeMethods.SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out factory);
            if (factory is null)
            {
                return null;
            }

            var flags = NativeMethods.SIIGBF_ICONONLY | NativeMethods.SIIGBF_BIGGERSIZEOK;
            if (factory.GetImage(new NativeMethods.SIZE(size, size), flags, out var bitmap) != 0 || bitmap == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return FromHBitmap(bitmap);
            }
            finally
            {
                NativeMethods.DeleteObject(bitmap);
            }
        }
        catch
        {
            return null;
        }
        finally
        {
            if (factory is not null)
            {
                try
                {
                    Marshal.ReleaseComObject(factory);
                }
                catch
                {
                    // Ignored.
                }
            }
        }
    }

    private static BitmapSource? LoadWithIconIndex(string path, int size, int iconIndex)
    {
        var large = IntPtr.Zero;
        var small = IntPtr.Zero;
        try
        {
            var request = (uint)((size & 0xFFFF) | (32 << 16));
            var hr = NativeMethods.SHDefExtractIcon(path, iconIndex, 0, out large, out small, request);
            if (hr < 0)
            {
                return null;
            }

            var handle = large != IntPtr.Zero ? large : small;
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            var bitmap = FromHIcon(handle);
            return bitmap is null ? null : Fit(bitmap, size);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (large != IntPtr.Zero)
            {
                NativeMethods.DestroyIcon(large);
            }

            if (small != IntPtr.Zero)
            {
                NativeMethods.DestroyIcon(small);
            }
        }
    }

    private static BitmapSource? LoadWithShellInfo(string path, int size)
    {
        var info = default(NativeMethods.SHFILEINFO);
        try
        {
            var flags = NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_LARGEICON;
            var attributes = 0u;
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                flags |= NativeMethods.SHGFI_USEFILEATTRIBUTES;
                attributes = NativeMethods.FILE_ATTRIBUTE_NORMAL;
            }

            var result = NativeMethods.SHGetFileInfo(path, attributes, ref info, (uint)Marshal.SizeOf<NativeMethods.SHFILEINFO>(), flags);
            if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
            {
                return null;
            }

            var bitmap = FromHIcon(info.hIcon);
            return bitmap is null ? null : Fit(bitmap, size);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (info.hIcon != IntPtr.Zero)
            {
                NativeMethods.DestroyIcon(info.hIcon);
            }
        }
    }

    private static BitmapSource? FromHBitmap(IntPtr hBitmap)
    {
        try
        {
            var source = Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            return Clone(source);
        }
        catch
        {
            return null;
        }
    }

    private static BitmapSource? FromHIcon(IntPtr hIcon)
    {
        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            return Clone(source);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Copies the pixels into an independent frozen bitmap so the native handle can be released safely.</summary>
    private static BitmapSource Clone(BitmapSource source)
    {
        var converted = source;
        if (source.Format != PixelFormats.Bgra32 && source.Format != PixelFormats.Pbgra32)
        {
            var format = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            format.Freeze();
            converted = format;
        }

        var width = converted.PixelWidth;
        var height = converted.PixelHeight;
        if (width <= 0 || height <= 0)
        {
            return converted;
        }

        var stride = width * 4;
        var pixels = new byte[stride * height];
        converted.CopyPixels(pixels, stride, 0);
        var copy = BitmapSource.Create(width, height, 96, 96, converted.Format, converted.Palette, pixels, stride);
        copy.Freeze();
        return copy;
    }

    private static BitmapSource Fit(BitmapSource source, int size)
    {
        BitmapSource bitmap = source.Format != PixelFormats.Pbgra32
            ? new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0)
            : source;
        bitmap.Freeze();

        if (bitmap.PixelWidth == size && bitmap.PixelHeight == size)
        {
            return bitmap;
        }

        var scale = Math.Min((double)size / bitmap.PixelWidth, (double)size / bitmap.PixelHeight);
        var width = Math.Max(1, (int)Math.Round(bitmap.PixelWidth * scale));
        var height = Math.Max(1, (int)Math.Round(bitmap.PixelHeight * scale));
        var offsetX = (size - width) / 2.0;
        var offsetY = (size - height) / 2.0;

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var context = visual.RenderOpen())
        {
            context.DrawImage(bitmap, new Rect(offsetX, offsetY, width, height));
        }

        var result = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        result.Render(visual);
        result.Freeze();
        return result;
    }

    private static string Expand(string path)
    {
        var trimmed = path.Trim().Trim('"');
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        return Environment.ExpandEnvironmentVariables(trimmed);
    }
}
