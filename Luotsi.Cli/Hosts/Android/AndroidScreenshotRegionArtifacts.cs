using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Hosts.Android;

internal sealed class AndroidScreenshotRegionArtifacts(ArtifactSession artifacts, IFileSystem fileSystem)
{
    private readonly ArtifactSession _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    public string ComputeRegionSha256(string path, ScreenshotAssertionRegion region)
    {
        var image = PngRgbaImage.Decode(ReadAllBytes(path));
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (var y = region.Y; y < region.Y + region.Height; y++)
        {
            var offset = ((y * image.Width) + region.X) * 4;
            hash.AppendData(image.Rgba, offset, region.Width * 4);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    public async Task<string?> WritePreviewAsync(string label, string screenshotPath, ScreenshotAssertionRegion? region)
    {
        if (region is null)
        {
            return null;
        }

        var preview = PngRgbaImage.Decode(ReadAllBytes(screenshotPath)).Crop(region);
        var previewFile = $"{Slugify(label)}-screenshot-region.png";
        await WritePngArtifactAsync(previewFile, preview).ConfigureAwait(false);
        return previewFile;
    }

    public async Task<string?> WriteDiffAsync(string label, string screenshotPath, string? baselineFile, ScreenshotAssertionRegion? region)
    {
        if (region is null || string.IsNullOrWhiteSpace(baselineFile) || !_fileSystem.FileExists(baselineFile))
        {
            return null;
        }

        var current = PngRgbaImage.Decode(ReadAllBytes(screenshotPath)).Crop(region);
        var baseline = PngRgbaImage.Decode(ReadAllBytes(baselineFile)).Crop(region);
        if (current.Width != baseline.Width || current.Height != baseline.Height)
        {
            return null;
        }

        var overlay = new byte[current.Rgba.Length];
        for (var offset = 0; offset < overlay.Length; offset += 4)
        {
            var same =
                current.Rgba[offset] == baseline.Rgba[offset] &&
                current.Rgba[offset + 1] == baseline.Rgba[offset + 1] &&
                current.Rgba[offset + 2] == baseline.Rgba[offset + 2] &&
                current.Rgba[offset + 3] == baseline.Rgba[offset + 3];
            overlay[offset] = same ? (byte)0 : (byte)255;
            overlay[offset + 1] = same ? (byte)180 : (byte)0;
            overlay[offset + 2] = 0;
            overlay[offset + 3] = 255;
        }

        var diffFile = $"{Slugify(label)}-screenshot-region-diff.png";
        await WritePngArtifactAsync(diffFile, new PngRgbaImage(current.Width, current.Height, overlay)).ConfigureAwait(false);
        return diffFile;
    }

    private async Task WritePngArtifactAsync(string fileName, PngRgbaImage image)
    {
        await using var output = _fileSystem.OpenWrite(Path.Join(_artifacts.Root, fileName));
        await output.WriteAsync(image.EncodePng()).ConfigureAwait(false);
        await _artifacts.RefreshIndexAsync().ConfigureAwait(false);
    }

    private byte[] ReadAllBytes(string path)
    {
        using var stream = _fileSystem.OpenRead(path);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static string Slugify(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            builder.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-');
        }

        return string.Join("-", builder.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));
    }

    private sealed record PngRgbaImage(int Width, int Height, byte[] Rgba)
    {
        public PngRgbaImage Crop(ScreenshotAssertionRegion region)
        {
            var cropped = new byte[region.Width * region.Height * 4];
            for (var y = 0; y < region.Height; y++)
            {
                Buffer.BlockCopy(
                    Rgba,
                    (((region.Y + y) * Width) + region.X) * 4,
                    cropped,
                    y * region.Width * 4,
                    region.Width * 4);
            }

            return new PngRgbaImage(region.Width, region.Height, cropped);
        }

        public byte[] EncodePng()
        {
            using var output = new MemoryStream();
            output.Write([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
            var ihdr = new byte[13];
            WriteBigEndian(ihdr, 0, Width);
            WriteBigEndian(ihdr, 4, Height);
            ihdr[8] = 8;
            ihdr[9] = 6;
            WritePngChunk(output, "IHDR", ihdr);
            using var raw = new MemoryStream();
            for (var y = 0; y < Height; y++)
            {
                raw.WriteByte(0);
                raw.Write(Rgba, y * Width * 4, Width * 4);
            }

            using var compressed = new MemoryStream();
            using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            {
                raw.Position = 0;
                raw.CopyTo(zlib);
            }

            WritePngChunk(output, "IDAT", compressed.ToArray());
            WritePngChunk(output, "IEND", []);
            return output.ToArray();
        }

        public static PngRgbaImage Decode(byte[] bytes)
        {
            var (width, height) = ReadPngDimensions(bytes);
            if (width is null || height is null)
            {
                throw new InvalidOperationException("Screenshot region assertions require a PNG image.");
            }

            var colorType = bytes[25];
            var bitDepth = bytes[24];
            var interlace = bytes[28];
            if (bitDepth != 8 || interlace != 0 || (colorType != 2 && colorType != 6 && colorType != 0 && colorType != 4 && colorType != 3))
            {
                throw new InvalidOperationException($"Screenshot region assertions support non-interlaced 8-bit grayscale, grayscale+alpha, indexed, RGB, or RGBA PNG files; got bit depth {bitDepth}, color type {colorType}, interlace {interlace}.");
            }

            var bytesPerPixel = colorType switch
            {
                6 => 4,
                4 => 2,
                2 => 3,
                _ => 1
            };
            var compressed = new MemoryStream();
            var palette = Array.Empty<byte>();
            var transparency = Array.Empty<byte>();
            var offset = 8;
            while (offset + 8 <= bytes.Length)
            {
                var length = ReadBigEndianInt32(bytes, offset);
                var type = Encoding.ASCII.GetString(bytes, offset + 4, 4);
                offset += 8;
                if (type == "IDAT")
                {
                    compressed.Write(bytes, offset, length);
                }
                else if (type == "PLTE")
                {
                    palette = bytes.Skip(offset).Take(length).ToArray();
                }
                else if (type == "tRNS")
                {
                    transparency = bytes.Skip(offset).Take(length).ToArray();
                }

                offset += length + 4;
                if (type == "IEND")
                {
                    break;
                }
            }

            compressed.Position = 0;
            using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
            using var raw = new MemoryStream();
            zlib.CopyTo(raw);
            return DecodeInflatedRows(raw.ToArray(), width.Value, height.Value, bytesPerPixel, colorType, palette, transparency);
        }

        private static PngRgbaImage DecodeInflatedRows(byte[] inflated, int width, int height, int bytesPerPixel, byte colorType, byte[] palette, byte[] transparency)
        {
            var stride = width * bytesPerPixel;
            var previous = new byte[stride];
            var current = new byte[stride];
            var rgba = new byte[width * height * 4];
            var source = 0;
            for (var y = 0; y < height; y++)
            {
                var filter = inflated[source++];
                Array.Copy(inflated, source, current, 0, stride);
                source += stride;
                Unfilter(current, previous, bytesPerPixel, filter);
                WriteRgbaRow(current, rgba, y, width, bytesPerPixel, colorType, palette, transparency);
                (previous, current) = (current, previous);
            }

            return new PngRgbaImage(width, height, rgba);
        }

        private static void WriteRgbaRow(byte[] current, byte[] rgba, int y, int width, int bytesPerPixel, byte colorType, byte[] palette, byte[] transparency)
        {
            for (var x = 0; x < width; x++)
            {
                var src = x * bytesPerPixel;
                var dst = ((y * width) + x) * 4;
                if (colorType == 0)
                {
                    rgba[dst] = rgba[dst + 1] = rgba[dst + 2] = current[src];
                    rgba[dst + 3] = 255;
                }
                else if (colorType == 4)
                {
                    rgba[dst] = rgba[dst + 1] = rgba[dst + 2] = current[src];
                    rgba[dst + 3] = current[src + 1];
                }
                else if (colorType == 3)
                {
                    WritePalettePixel(current[src], rgba, dst, palette, transparency);
                }
                else
                {
                    rgba[dst] = current[src];
                    rgba[dst + 1] = current[src + 1];
                    rgba[dst + 2] = current[src + 2];
                    rgba[dst + 3] = colorType == 6 ? current[src + 3] : (byte)255;
                }
            }
        }

        private static void WritePalettePixel(byte paletteIndex, byte[] rgba, int dst, byte[] palette, byte[] transparency)
        {
            var paletteOffset = paletteIndex * 3;
            if (paletteOffset + 2 >= palette.Length)
            {
                throw new InvalidOperationException($"PNG palette index {paletteIndex} was outside the palette.");
            }

            rgba[dst] = palette[paletteOffset];
            rgba[dst + 1] = palette[paletteOffset + 1];
            rgba[dst + 2] = palette[paletteOffset + 2];
            rgba[dst + 3] = paletteIndex < transparency.Length ? transparency[paletteIndex] : (byte)255;
        }

        private static void Unfilter(byte[] row, byte[] previous, int bytesPerPixel, int filter)
        {
            for (var i = 0; i < row.Length; i++)
            {
                var left = i >= bytesPerPixel ? row[i - bytesPerPixel] : 0;
                var up = previous[i];
                var upLeft = i >= bytesPerPixel ? previous[i - bytesPerPixel] : 0;
                row[i] = filter switch
                {
                    0 => row[i],
                    1 => unchecked((byte)(row[i] + left)),
                    2 => unchecked((byte)(row[i] + up)),
                    3 => unchecked((byte)(row[i] + ((left + up) / 2))),
                    4 => unchecked((byte)(row[i] + Paeth(left, up, upLeft))),
                    _ => throw new InvalidOperationException($"Unsupported PNG filter {filter}.")
                };
            }
        }

        private static (int? Width, int? Height) ReadPngDimensions(byte[] bytes)
        {
            if (bytes.Length < 24 ||
                bytes[0] != 0x89 ||
                bytes[1] != 0x50 ||
                bytes[2] != 0x4e ||
                bytes[3] != 0x47)
            {
                return (null, null);
            }

            return (ReadBigEndianInt32(bytes, 16), ReadBigEndianInt32(bytes, 20));
        }

        private static int Paeth(int left, int up, int upLeft)
        {
            var estimate = left + up - upLeft;
            var leftDistance = Math.Abs(estimate - left);
            var upDistance = Math.Abs(estimate - up);
            var upLeftDistance = Math.Abs(estimate - upLeft);
            return leftDistance <= upDistance && leftDistance <= upLeftDistance ? left : upDistance <= upLeftDistance ? up : upLeft;
        }

        private static void WritePngChunk(Stream output, string type, byte[] data)
        {
            var length = new byte[4];
            WriteBigEndian(length, 0, data.Length);
            var typeBytes = Encoding.ASCII.GetBytes(type);
            output.Write(length);
            output.Write(typeBytes);
            output.Write(data);
            var crc = new Crc32();
            crc.Append(typeBytes);
            crc.Append(data);
            var crcBytes = new byte[4];
            WriteBigEndian(crcBytes, 0, unchecked((int)crc.Value));
            output.Write(crcBytes);
        }

        private static int ReadBigEndianInt32(byte[] bytes, int offset) =>
            bytes[offset] << 24 |
            bytes[offset + 1] << 16 |
            bytes[offset + 2] << 8 |
            bytes[offset + 3];

        private static void WriteBigEndian(byte[] bytes, int offset, int value)
        {
            bytes[offset] = (byte)(value >> 24);
            bytes[offset + 1] = (byte)(value >> 16);
            bytes[offset + 2] = (byte)(value >> 8);
            bytes[offset + 3] = (byte)value;
        }
    }

    private sealed class Crc32
    {
        private static readonly uint[] Table = CreateTable();

        private uint _value = 0xffffffff;

        public uint Value => _value ^ 0xffffffff;

        public void Append(byte[] bytes)
        {
            foreach (var value in bytes)
            {
                _value = Table[(_value ^ value) & 0xff] ^ (_value >> 8);
            }
        }

        private static uint[] CreateTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < table.Length; i++)
            {
                var value = i;
                for (var bit = 0; bit < 8; bit++)
                {
                    value = (value & 1) == 1 ? 0xedb88320 ^ (value >> 1) : value >> 1;
                }

                table[i] = value;
            }

            return table;
        }
    }
}
