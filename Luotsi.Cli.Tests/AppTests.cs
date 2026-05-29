using System.Text.Json;
using System.IO.Compression;
using System.Text;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Tests;

public sealed partial class AppTests
{
    private static readonly JsonSerializerOptions TestJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private static string CreateUiDump(string text) =>
        $"<hierarchy><node text=\"{text}\" content-desc=\"\" resource-id=\"id/{text}\" class=\"android.widget.TextView\" enabled=\"true\" clickable=\"false\" bounds=\"[0,0][100,100]\" /></hierarchy>";

    private static string CreateDeviceFingerprintShellOutput(string serial, string model, string androidRelease, string sdk, string fingerprint, string abi, string currentFocus) =>
        string.Join(
            "\n",
            "__LUOTSI_DEVICE_FINGERPRINT__SERIAL__",
            serial,
            "__LUOTSI_DEVICE_FINGERPRINT__MODEL__",
            model,
            "__LUOTSI_DEVICE_FINGERPRINT__ANDROID_RELEASE__",
            androidRelease,
            "__LUOTSI_DEVICE_FINGERPRINT__SDK__",
            sdk,
            "__LUOTSI_DEVICE_FINGERPRINT__FINGERPRINT__",
            fingerprint,
            "__LUOTSI_DEVICE_FINGERPRINT__ABI__",
            abi,
            "__LUOTSI_DEVICE_FINGERPRINT__CURRENT_FOCUS__",
            currentFocus);

    private static JsonElement SerializeToJsonElement<T>(T value)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value, TestJsonOptions));
        return document.RootElement.Clone();
    }

    private static string CreateUiDumpWithNodes(params string[] nodes) => $"<hierarchy>{string.Join(string.Empty, nodes)}</hierarchy>";

    private static string CreateUiNode(string text, string contentDescription, string className, bool clickable, int left, int top, int right, int bottom) =>
        $"<node text=\"{text}\" content-desc=\"{contentDescription}\" resource-id=\"\" class=\"{className}\" enabled=\"true\" clickable=\"{clickable.ToString().ToLowerInvariant()}\" bounds=\"[{left},{top}][{right},{bottom}]\" />";

    private static ScreenState CreateScreenState(DateTimeOffset capturedAt, string text) =>
        new(capturedAt, 1, [new ScreenElement(text, null, $"id/{text}", "android.widget.TextView", true, true, 0, 0, 100, 100)]);

    private static byte[] CreatePngHeader(int width, int height)
    {
        var bytes = new byte[24];
        bytes[0] = 0x89;
        bytes[1] = 0x50;
        bytes[2] = 0x4e;
        bytes[3] = 0x47;
        WriteBigEndian(bytes, 16, width);
        WriteBigEndian(bytes, 20, height);
        return bytes;
    }

    private static byte[] CreatePngRgba(int width, int height, byte[] rgba)
    {
        using var output = new MemoryStream();
        output.Write([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
        WriteChunk(output, "IHDR", CreateIhdr(width, height));
        using var raw = new MemoryStream();
        for (var y = 0; y < height; y++)
        {
            raw.WriteByte(0);
            raw.Write(rgba, y * width * 4, width * 4);
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            raw.Position = 0;
            raw.CopyTo(zlib);
        }

        WriteChunk(output, "IDAT", compressed.ToArray());
        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    private static byte[] CreateIhdr(int width, int height)
    {
        var ihdr = new byte[13];
        WriteBigEndian(ihdr, 0, width);
        WriteBigEndian(ihdr, 4, height);
        ihdr[8] = 8;
        ihdr[9] = 6;
        return ihdr;
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4];
        WriteBigEndian(length, 0, data.Length);
        stream.Write(length);
        stream.Write(Encoding.ASCII.GetBytes(type));
        stream.Write(data);
        stream.Write(new byte[4]);
    }

    private static void WriteBigEndian(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)(value >> 24);
        bytes[offset + 1] = (byte)(value >> 16);
        bytes[offset + 2] = (byte)(value >> 8);
        bytes[offset + 3] = (byte)value;
    }
}
