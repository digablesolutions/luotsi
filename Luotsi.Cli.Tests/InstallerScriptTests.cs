using Xunit;

namespace Luotsi.Cli.Tests;

public sealed partial class AppTests
{
    [Fact]
    public void PowerShell_Installer_Preserves_Previous_Install_Until_Command_Shapes_Are_Committed()
    {
        var script = ReadRepositoryText("scripts", "install.ps1");

        AssertOrdered(
            script,
            "Move-Item -LiteralPath $payloadRoot -Destination $currentDirectory",
            "$viewExtras = Install-ViewExtras $currentDirectory $rid $SkipFfmpeg.IsPresent",
            "Write-CommandShim $commandPath",
            "Write-Manifest $manifestPath $resolvedInstallRoot $binDirectory $commandPath $resolvedTag $rid $archiveName $archiveUrl $checksumUrl $viewExtras",
            "$installCommitted = $true",
            "Remove-Item -LiteralPath $previousDirectory -Recurse -Force -ErrorAction SilentlyContinue");

        Assert.Contains("[switch]$SkipFfmpeg", script, StringComparison.Ordinal);
        Assert.Contains("ffmpeg_staged = $ViewExtras.ffmpeg_staged", script, StringComparison.Ordinal);

        var catchBlock = Slice(script, "catch {", "finally {");
        Assert.Contains("if (-not $installCommitted)", catchBlock, StringComparison.Ordinal);
        AssertOrdered(
            catchBlock,
            "Remove-Item -LiteralPath $currentDirectory -Recurse -Force -ErrorAction SilentlyContinue",
            "Move-Item -LiteralPath $previousDirectory -Destination $currentDirectory");
    }

    [Fact]
    public void Shell_Installer_Preserves_Previous_Install_Until_Command_Shapes_Are_Committed()
    {
        var script = ReadRepositoryText("scripts", "install.sh");

        AssertOrdered(
            script,
            "mv \"$payload_dir\" \"$CURRENT_DIR\"",
            "install_view_extras \"$CURRENT_DIR\" \"$RID\" \"$SKIP_FFMPEG\"",
            "write_command_shim \"$COMMAND_PATH\"",
            "write_manifest \"$MANIFEST_PATH\" \"$RESOLVED_INSTALL_ROOT\" \"$BIN_DIR\" \"$COMMAND_PATH\" \"$RESOLVED_TAG\" \"$RID\" \"$ARCHIVE_NAME\" \"$ARCHIVE_URL\" \"$CHECKSUM_URL\" \"$VIEW_EXTRAS\" \"$FFMPEG_STAGED\" \"$FFMPEG_PATH\" \"$FFMPEG_DETAIL\"",
            "install_committed=1",
            "rm -rf \"$PREVIOUS_DIR\" || true");

        Assert.Contains("--skip-ffmpeg", script, StringComparison.Ordinal);
        Assert.Contains("\"ffmpeg_staged\": $ffmpeg_staged", script, StringComparison.Ordinal);
        Assert.Contains("json_string()", script, StringComparison.Ordinal);
        Assert.Contains("\"ffmpeg_detail\": $escaped_ffmpeg_detail", script, StringComparison.Ordinal);

        var restoreBlock = Slice(script, "restore_previous() {", "cleanup() {");
        Assert.Contains("if [ \"$install_committed\" -ne 0 ]; then", restoreBlock, StringComparison.Ordinal);
        AssertOrdered(
            restoreBlock,
            "rm -rf \"$CURRENT_DIR\"",
            "mv \"$PREVIOUS_DIR\" \"$CURRENT_DIR\"");
    }

    private static string ReadRepositoryText(params string[] segments)
    {
        var path = FindRepositoryRoot();
        foreach (var segment in segments)
        {
            path = Path.Join(path, segment);
        }

        return File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string Slice(string text, string startMarker, string endMarker)
    {
        var startIndex = text.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Did not find '{startMarker}' in the script under test.");

        var endIndex = text.IndexOf(endMarker, startIndex + startMarker.Length, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex, $"Did not find '{endMarker}' after '{startMarker}' in the script under test.");

        return text[startIndex..endIndex];
    }

    private static void AssertOrdered(string text, params string[] snippets)
    {
        var searchIndex = 0;
        foreach (var snippet in snippets)
        {
            var snippetIndex = text.IndexOf(snippet, searchIndex, StringComparison.Ordinal);
            Assert.True(snippetIndex >= 0, $"Did not find '{snippet}' in the expected order.");
            searchIndex = snippetIndex + snippet.Length;
        }
    }
}
