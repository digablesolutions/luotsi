using System.Runtime.InteropServices;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Infrastructure.System;

namespace Luotsi.Cli.Artifacts;

internal static class ArtifactWorkspacePaths
{
    private const string WorkspaceFolderName = "luotsi";
    private const string ArtifactFolderName = "artifacts";

    public static string ResolveDefaultWorkspaceRoot(IFileSystem fileSystem, IEnvironmentVariables? environment)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        if (ShouldUseTempFallback(fileSystem, environment))
        {
            return ResolveTempFallback(fileSystem);
        }

        if (TryResolveUserLocalWorkspaceRoot(environment, out var workspaceRoot))
        {
            return workspaceRoot!;
        }

        return ResolveTempFallback(fileSystem);
    }

    public static string ResolveDefaultRunArtifactBaseDirectory(IFileSystem fileSystem, IEnvironmentVariables? environment)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        return Path.Join(ResolveDefaultWorkspaceRoot(fileSystem, environment), ArtifactFolderName);
    }

    private static bool ShouldUseTempFallback(IFileSystem fileSystem, IEnvironmentVariables? environment) =>
        environment is null ||
        (fileSystem is not PhysicalFileSystem && environment is SystemEnvironmentVariables);

    private static string ResolveTempFallback(IFileSystem fileSystem) =>
        Path.Join(fileSystem.GetTempPath(), WorkspaceFolderName);

    private static bool TryResolveUserLocalWorkspaceRoot(IEnvironmentVariables? environment, out string? workspaceRoot)
    {
        workspaceRoot = null;
        if (environment is null)
        {
            return false;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var localAppData = environment.GetEnvironmentVariable("LOCALAPPDATA");
            if (string.IsNullOrWhiteSpace(localAppData))
            {
                return false;
            }

            workspaceRoot = Path.Join(localAppData, "Luotsi");
            return true;
        }

        var home = environment.GetEnvironmentVariable("HOME");
        if (string.IsNullOrWhiteSpace(home))
        {
            return false;
        }

        workspaceRoot = Path.Join(home, ".local", "share", WorkspaceFolderName);
        return true;
    }
}
