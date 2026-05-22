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
    public void BitmapIconDecoder_Returns_Null_For_Invalid_Data()
    {
        using var stream = new MemoryStream("not a bitmap"u8.ToArray());

        var icon = BitmapIconDecoder.Decode(stream);

        Assert.Null(icon);
    }
}
