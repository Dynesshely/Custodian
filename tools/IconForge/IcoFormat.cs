using System.Globalization;
using System.IO;
using System.Windows.Media.Imaging;

namespace Custodian.IconForge;

/// <summary>
/// Minimal, dependency-free ICO reader/writer.
///
/// Layout: ICONDIR (6 bytes) followed by one ICONDIRENTRY (16 bytes) per image,
/// then the payloads. Frames up to 64 px are 32 bpp bitmaps (BITMAPINFOHEADER +
/// bottom-up BGRA + 1 bpp AND mask, height doubled); 128 px and above are PNG.
/// The 512 px frame keeps 0 in the directory width/height bytes (that field caps
/// at 256) and Windows reads the real size from the PNG header - the usual
/// convention for large frames.
/// </summary>
internal static class IcoFormat
{
    public sealed record Entry(int Size, bool IsPng, byte[] Data);

    private const int IconDirSize = 6;
    private const int IconDirEntrySize = 16;

    /// <summary>Frames the finished icon must contain.</summary>
    public static readonly int[] RequiredSizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256, 512 };

    public static byte[] Build(IReadOnlyList<Entry> entries)
    {
        if (entries.Count == 0)
        {
            throw new ArgumentException("ICO 至少需要一个条目。", nameof(entries));
        }

        int payloadOffset = IconDirSize + IconDirEntrySize * entries.Count;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)entries.Count);

        int offset = payloadOffset;
        foreach (Entry entry in entries)
        {
            byte dimension = entry.Size >= 256 ? (byte)0 : (byte)entry.Size;
            writer.Write(dimension);
            writer.Write(dimension);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write((uint)entry.Data.Length);
            writer.Write((uint)offset);
            offset += entry.Data.Length;
        }

        foreach (Entry entry in entries)
        {
            writer.Write(entry.Data);
        }

        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>Builds a 32 bpp BMP payload (BITMAPINFOHEADER + XOR bitmap + AND mask).</summary>
    public static byte[] BuildBmpEntry(BitmapSource image, int size)
    {
        if (image.PixelWidth != size || image.PixelHeight != size)
        {
            throw new ArgumentException($"位图必须是 {size}x{size}，实际 {image.PixelWidth}x{image.PixelHeight}。");
        }

        if (image.Format != System.Windows.Media.PixelFormats.Bgra32)
        {
            image = IconRender.ToBgra32(image);
        }

        int stride = size * 4;
        var pixels = new byte[stride * size];
        image.CopyPixels(pixels, stride, 0);

        int maskStride = (size + 31) / 32 * 4;
        int maskSize = maskStride * size;
        int xorSize = stride * size;

        using var stream = new MemoryStream(40 + xorSize + maskSize);
        using var writer = new BinaryWriter(stream);

        writer.Write(40);
        writer.Write(size);
        writer.Write(size * 2);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(0);
        writer.Write(xorSize);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);

        for (int y = size - 1; y >= 0; y--)
        {
            writer.Write(pixels, y * stride, stride);
        }

        var maskRow = new byte[maskStride];
        for (int y = size - 1; y >= 0; y--)
        {
            Array.Clear(maskRow);
            int rowStart = y * stride;
            for (int x = 0; x < size; x++)
            {
                if (pixels[rowStart + x * 4 + 3] == 0)
                {
                    maskRow[x >> 3] |= (byte)(0x80 >> (x & 7));
                }
            }

            writer.Write(maskRow, 0, maskStride);
        }

        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>Prints the entry table of an .ico file and validates its structure.</summary>
    public static int Inspect(string path, TextWriter output)
    {
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            output.WriteLine($"找不到文件: {fullPath}");
            return 2;
        }

        byte[] bytes = File.ReadAllBytes(fullPath);
        var problems = new List<string>();

        output.WriteLine($"文件      : {fullPath}");
        output.WriteLine($"大小      : {bytes.Length:N0} 字节");

        if (bytes.Length < IconDirSize)
        {
            output.WriteLine("错误      : 文件太小，不是 ICO。");
            return 1;
        }

        int reserved = ReadUInt16(bytes, 0);
        int type = ReadUInt16(bytes, 2);
        int count = ReadUInt16(bytes, 4);

        output.WriteLine($"ICONDIR   : reserved={reserved} type={type} count={count}");
        if (reserved != 0) problems.Add("ICONDIR.reserved 必须为 0");
        if (type != 1) problems.Add($"ICONDIR.type 必须为 1（图标），实际 {type}");
        if (count == 0) problems.Add("ICONDIR.count 为 0");
        if (IconDirSize + count * IconDirEntrySize > bytes.Length)
        {
            output.WriteLine("错误      : 条目表超出文件长度。");
            return 1;
        }

        output.WriteLine();
        output.WriteLine(" idx  size      format  bytes     offset    detail");
        output.WriteLine(" ---  --------  ------  --------  --------  -------------------------------------------");

        int previousEnd = IconDirSize + count * IconDirEntrySize;
        var seenSizes = new List<int>();

        for (int i = 0; i < count; i++)
        {
            int entryOffset = IconDirSize + i * IconDirEntrySize;
            int declaredWidth = bytes[entryOffset] == 0 ? 256 : bytes[entryOffset];
            int declaredHeight = bytes[entryOffset + 1] == 0 ? 256 : bytes[entryOffset + 1];
            int colourCount = bytes[entryOffset + 2];
            int entryReserved = bytes[entryOffset + 3];
            ushort planes = (ushort)ReadUInt16(bytes, entryOffset + 4);
            ushort bitCount = (ushort)ReadUInt16(bytes, entryOffset + 6);
            long dataSize = ReadUInt32(bytes, entryOffset + 8);
            long dataOffset = ReadUInt32(bytes, entryOffset + 12);

            if (colourCount != 0) problems.Add($"#{i} bColorCount 应为 0");
            if (entryReserved != 0) problems.Add($"#{i} bReserved 应为 0");
            if (planes != 1) problems.Add($"#{i} wPlanes 应为 1");
            if (bitCount != 32) problems.Add($"#{i} wBitCount 应为 32");
            if (dataOffset < previousEnd) problems.Add($"#{i} 数据与前一条目重叠");
            if (dataOffset + dataSize > bytes.Length) problems.Add($"#{i} 数据超出文件长度");

            string format;
            string detail;
            int realSize = declaredWidth;

            if (dataOffset + 8 <= bytes.Length && bytes[dataOffset] == 0x89 && bytes[dataOffset + 1] == 0x50
                && bytes[dataOffset + 2] == 0x4E && bytes[dataOffset + 3] == 0x47)
            {
                format = "PNG";
                int pngWidth = ReadInt32BigEndian(bytes, (int)dataOffset + 16);
                int pngHeight = ReadInt32BigEndian(bytes, (int)dataOffset + 20);
                int bitDepth = bytes[dataOffset + 24];
                int colourType = bytes[dataOffset + 25];
                realSize = pngWidth;
                detail = $"IHDR {pngWidth}x{pngHeight} depth={bitDepth} colour={colourType}";

                if (pngWidth != pngHeight) problems.Add($"#{i} PNG 不是正方形");
                if (bitDepth != 8 || colourType != 6) problems.Add($"#{i} PNG 不是 8 位 RGBA");
                if (pngWidth < 256 && pngWidth != declaredWidth)
                {
                    problems.Add($"#{i} 目录宽度 {declaredWidth} 与 PNG {pngWidth} 不一致");
                }

                if (pngWidth >= 256 && bytes[entryOffset] != 0)
                {
                    problems.Add($"#{i} 大尺寸条目的目录宽高字节应为 0");
                }
            }
            else if (dataOffset + 40 <= bytes.Length && ReadUInt32(bytes, (int)dataOffset) == 40)
            {
                format = "BMP";
                int biWidth = ReadInt32(bytes, (int)dataOffset + 4);
                int biHeight = ReadInt32(bytes, (int)dataOffset + 8);
                int biPlanes = ReadUInt16(bytes, (int)dataOffset + 12);
                int biBitCount = ReadUInt16(bytes, (int)dataOffset + 14);
                long biSizeImage = ReadUInt32(bytes, (int)dataOffset + 20);
                int maskStride = (declaredWidth + 31) / 32 * 4;
                long expectedPayload = 40 + (long)declaredWidth * declaredHeight * 4 + (long)maskStride * declaredHeight;
                detail = $"biHeight={biHeight} biPlanes={biPlanes} biBitCount={biBitCount} "
                       + $"mask={maskStride * declaredHeight}B expected={expectedPayload}B";

                if (biWidth != declaredWidth || biHeight != declaredWidth * 2)
                {
                    problems.Add($"#{i} BITMAPINFOHEADER 尺寸错误");
                }

                if (biPlanes != 1) problems.Add($"#{i} biPlanes 必须为 1");
                if (biBitCount != 32) problems.Add($"#{i} biBitCount 必须为 32");
                if (biSizeImage != (long)declaredWidth * declaredHeight * 4) problems.Add($"#{i} biSizeImage 不符");
                if (dataSize != expectedPayload) problems.Add($"#{i} dwBytesInRes 与预期不符");
            }
            else
            {
                format = "????";
                detail = "无法识别的载荷";
                problems.Add($"#{i} 载荷既不是 PNG 也不是 32bpp BMP");
            }

            output.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                " {0,3}  {1,4}x{1,-4} {2,-6}  {3,7:N0}  {4,8:N0}  {5}",
                i, realSize, format, dataSize, dataOffset, detail));

            previousEnd = (int)(dataOffset + dataSize);
            seenSizes.Add(realSize);
        }

        output.WriteLine();

        try
        {
            var decoder = new IconBitmapDecoder(new Uri(fullPath), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var first = decoder.Frames.Count > 0 ? decoder.Frames[0] : null;
            output.WriteLine($"WPF 解码   : frames={decoder.Frames.Count}"
                + (first is null ? string.Empty : $" 首帧={first.PixelWidth}x{first.PixelHeight} {first.Format}"));
        }
        catch (Exception ex)
        {
            problems.Add($"WPF IconBitmapDecoder 解码失败: {ex.Message}");
        }

        foreach (int size in RequiredSizes)
        {
            if (!seenSizes.Contains(size))
            {
                problems.Add($"缺少 {size}px 条目");
            }
        }

        if (seenSizes.Count >= 2 && !seenSizes.SequenceEqual(seenSizes.OrderBy(s => s)))
        {
            problems.Add("条目未按尺寸升序排列");
        }

        if (problems.Count == 0)
        {
            output.WriteLine($"校验      : 通过（{count} 个条目：{string.Join(", ", seenSizes)}）");
            return 0;
        }

        output.WriteLine($"校验      : 发现 {problems.Count} 个问题");
        foreach (string problem in problems)
        {
            output.WriteLine($"  - {problem}");
        }

        return 1;
    }

    private static int ReadUInt16(byte[] bytes, int offset) => bytes[offset] | (bytes[offset + 1] << 8);

    private static int ReadInt32(byte[] bytes, int offset) =>
        bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16) | (bytes[offset + 3] << 24);

    private static long ReadUInt32(byte[] bytes, int offset) => (uint)ReadInt32(bytes, offset);

    private static int ReadInt32BigEndian(byte[] bytes, int offset) =>
        (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
}
