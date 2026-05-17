using System.Buffers.Binary;
using System.Reflection;

namespace Luotsi.Cli.View;

internal interface IViewWindowIconProvider
{
    ViewWindowIcon? GetDefaultIcon();
}

internal sealed class LuotsiWindowIconProvider : IViewWindowIconProvider
{
    private const string DefaultIconResourceName = "Luotsi.Cli.Assets.luotsi-icon.bmp";

    public ViewWindowIcon? GetDefaultIcon()
    {
        var assembly = typeof(LuotsiWindowIconProvider).Assembly;
        using var stream = assembly.GetManifestResourceStream(DefaultIconResourceName);
        return stream is null ? null : BitmapIconDecoder.Decode(stream);
    }
}

internal sealed record ViewWindowIcon(int Width, int Height, int Pitch, byte[] ArgbPixels);

internal static class BitmapIconDecoder
{
    private const ushort BitmapFileMagic = 0x4D42;
    private const ushort ExpectedBitsPerPixel = 32;
    private const uint BiRgbCompression = 0;

    public static ViewWindowIcon? Decode(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var data = memory.ToArray();

        if (data.Length < 54 ||
            BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(0, 2)) != BitmapFileMagic)
        {
            return null;
        }

        var pixelOffset = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(10, 4));
        var dibHeaderSize = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(14, 4));
        if (dibHeaderSize < 40 || data.Length < 14 + dibHeaderSize)
        {
            return null;
        }

        var width = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(18, 4));
        var rawHeight = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(22, 4));
        var planes = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(26, 2));
        var bitsPerPixel = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(28, 2));
        var compression = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(30, 4));
        if (width <= 0 ||
            rawHeight == 0 ||
            planes != 1 ||
            bitsPerPixel != ExpectedBitsPerPixel ||
            compression != BiRgbCompression ||
            pixelOffset < 0 ||
            pixelOffset >= data.Length)
        {
            return null;
        }

        var height = Math.Abs(rawHeight);
        var sourceStride = width * 4;
        if (data.Length - pixelOffset < sourceStride * height)
        {
            return null;
        }

        var pixels = new byte[sourceStride * height];
        var bottomUp = rawHeight > 0;
        for (var row = 0; row < height; row++)
        {
            var sourceRow = bottomUp ? height - 1 - row : row;
            var source = data.AsSpan(pixelOffset + sourceRow * sourceStride, sourceStride);
            var destination = pixels.AsSpan(row * sourceStride, sourceStride);
            for (var column = 0; column < width; column++)
            {
                var sourceIndex = column * 4;
                destination[sourceIndex] = source[sourceIndex + 2];
                destination[sourceIndex + 1] = source[sourceIndex + 1];
                destination[sourceIndex + 2] = source[sourceIndex];
                destination[sourceIndex + 3] = source[sourceIndex + 3];
            }
        }

        return new ViewWindowIcon(width, height, sourceStride, pixels);
    }
}
