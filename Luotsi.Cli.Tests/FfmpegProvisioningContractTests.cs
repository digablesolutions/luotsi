using FFmpeg.AutoGen;
using Xunit;

namespace Luotsi.Cli.Tests;

public sealed partial class AppTests
{
    [Fact]
    public void Ffmpeg_Provisioners_Match_Managed_Binding_Release_Line()
    {
        // Lock the native provisioning contract to the installed managed ABI.
        Assert.Equal(63, ffmpeg.LIBAVCODEC_VERSION_MAJOR);
        Assert.Equal(61, ffmpeg.LIBAVUTIL_VERSION_MAJOR);
        Assert.Equal(10, ffmpeg.LIBSWSCALE_VERSION_MAJOR);
        var stager = ReadRepositoryText("ffmpeg", "download-ffmpeg.ps1");
        Assert.Contains("[string]$Version = \"9.0\"", stager, StringComparison.Ordinal);
        Assert.Contains("avcodec-63.dll", stager, StringComparison.Ordinal);
        Assert.Contains("libavcodec.so.63", stager, StringComparison.Ordinal);
        Assert.Contains("libavcodec.63.dylib", stager, StringComparison.Ordinal);
        Assert.Contains("Existing FFmpeg libraries do not match AutoGen 9", stager, StringComparison.Ordinal);
        Assert.Contains("Existing staged libraries were not replaced", stager, StringComparison.Ordinal);
        var installer = ReadRepositoryText("scripts", "install.sh");
        Assert.Contains("ffmpeg-n9.0-latest-linux64-lgpl-shared-9.0.tar.xz", installer, StringComparison.Ordinal);
        Assert.DoesNotContain("ffmpeg-n8.1", installer, StringComparison.Ordinal);
    }
}
