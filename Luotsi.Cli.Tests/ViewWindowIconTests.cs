using System.Text;
using Luotsi.Cli.View.Rendering;
using Xunit;

namespace Luotsi.Cli.Tests;

public sealed class ViewWindowIconTests
{
    [Fact]
    public void LuotsiWindowIconProvider_Returns_Embedded_Default_Icon()
    {
        var icon = new LuotsiWindowIconProvider().GetDefaultIcon();

        Assert.NotNull(icon);
        Assert.Equal(64, icon.Width);
        Assert.Equal(64, icon.Height);
        Assert.Equal(icon.Width * 4, icon.Pitch);
        Assert.Equal(icon.Pitch * icon.Height, icon.ArgbPixels.Length);
        Assert.Contains(icon.ArgbPixels, value => value != 0);
    }

    [Fact]
    public void WindowsApplicationIcon_Asset_Is_Present()
    {
        var assetDirectory = GetCliAssetDirectory();
        var markPath = Path.Combine(assetDirectory, "luotsi-mark.svg");
        var iconPath = Path.Combine(assetDirectory, "luotsi-icon.ico");

        Assert.True(File.Exists(markPath), $"Missing Luotsi mark source asset at {markPath}.");
        Assert.True(File.Exists(iconPath), $"Missing Windows application icon at {iconPath}.");
        Assert.True(new FileInfo(iconPath).Length > 0);
    }

    [Fact]
    public void BitmapIconDecoder_Returns_Null_For_Invalid_Data()
    {
        using var stream = new MemoryStream("not a bitmap"u8.ToArray());

        var icon = BitmapIconDecoder.Decode(stream);

        Assert.Null(icon);
    }

    private static string GetCliAssetDirectory() => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..",
        "..",
        "..",
        "..",
        "Luotsi.Cli",
        "Assets"));
}
