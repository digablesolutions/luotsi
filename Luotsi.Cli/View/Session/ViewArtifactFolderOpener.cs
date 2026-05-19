using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Luotsi.Cli.View.Session;

public interface IArtifactFolderOpener
{
    Task OpenAsync(string path);
}

internal sealed class SystemArtifactFolderOpener : IArtifactFolderOpener
{
    public Task OpenAsync(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var startInfo = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new ProcessStartInfo("explorer.exe", fullPath)
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? new ProcessStartInfo("open", fullPath)
                : new ProcessStartInfo("xdg-open", fullPath);

        startInfo.UseShellExecute = false;
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to open artifact folder '{fullPath}'.");
        return Task.CompletedTask;
    }
}