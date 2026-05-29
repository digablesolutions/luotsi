using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;

namespace Luotsi.Cli.Artifacts;

internal static class ArtifactRootResolver
{
    public static string ResolveSearchRoot(
        IFileSystem fileSystem,
        string? searchRoot,
        IEnvironmentVariables? environment = null,
        bool preferWorkspaceHome = false)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        return string.IsNullOrWhiteSpace(searchRoot)
            ? preferWorkspaceHome
                ? ArtifactWorkspacePaths.ResolveDefaultRunArtifactBaseDirectory(fileSystem, environment)
                : Path.Join(fileSystem.GetTempPath(), "luotsi")
            : searchRoot;
    }

    public static string ResolveArtifactRoot(
        IFileSystem fileSystem,
        string target,
        string? searchRoot,
        IEnvironmentVariables? environment = null,
        bool preferWorkspaceHome = false)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        if (string.IsNullOrWhiteSpace(target))
        {
            throw new UsageException("Artifact command requires <artifact-root-or-run-id>.");
        }

        if (fileSystem.DirectoryExists(target))
        {
            return target;
        }

        var baseRoot = ResolveSearchRoot(fileSystem, searchRoot, environment, preferWorkspaceHome);
        if (!fileSystem.DirectoryExists(baseRoot))
        {
            throw new UsageException($"Artifact root '{target}' does not exist, and search root '{baseRoot}' does not exist.");
        }

        var matches = ResolveArtifactRootCandidates(fileSystem, baseRoot)
            .Where(root => string.Equals(GetArtifactRootName(root), target, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return matches.Length switch
        {
            1 => matches[0]!,
            0 => throw new UsageException($"Artifact root or run id '{target}' was not found under '{baseRoot}'."),
            _ => throw new UsageException($"Artifact run id '{target}' matched multiple roots under '{baseRoot}'. Use the full artifact root path.")
        };
    }

    public static string ResolveLatestArtifactRoot(
        IFileSystem fileSystem,
        string? searchRoot,
        IEnvironmentVariables? environment = null,
        bool preferWorkspaceHome = false)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        var baseRoot = ResolveSearchRoot(fileSystem, searchRoot, environment, preferWorkspaceHome);
        if (!fileSystem.DirectoryExists(baseRoot))
        {
            throw new UsageException($"Artifact search root '{baseRoot}' does not exist.");
        }

        var latestRoot = ResolveArtifactRootCandidates(fileSystem, baseRoot)
            .OrderByDescending(GetArtifactRootName, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(static root => root, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(latestRoot))
        {
            throw new UsageException($"No artifact roots were found under '{baseRoot}'.");
        }

        return latestRoot;
    }

    public static string[] ResolveArtifactRootCandidates(IFileSystem fileSystem, string baseRoot)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        var fullBase = TrimDirectoryEnding(Path.GetFullPath(baseRoot));
        var files = fileSystem.GetFiles(baseRoot, "*", SearchOption.AllDirectories);
        if (files.Any(file => IsDirectArtifactRootMarker(fullBase, file)))
        {
            return [baseRoot];
        }

        return files
            .Select(file => ResolveDirectChildRoot(baseRoot, fullBase, file))
            .Where(static root => !string.IsNullOrWhiteSpace(root))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray()!;
    }

    private static string GetArtifactRootName(string root) =>
        Path.GetFileName(TrimDirectoryEnding(root));

    private static bool IsDirectArtifactRootMarker(string fullBase, string file)
    {
        var directory = Path.GetDirectoryName(file);
        return directory is not null &&
            string.Equals(TrimDirectoryEnding(directory), fullBase, StringComparison.OrdinalIgnoreCase) &&
            IsArtifactRootMarker(Path.GetFileName(file));
    }

    private static bool IsArtifactRootMarker(string fileName) =>
        string.Equals(fileName, ArtifactSession.ArtifactHtmlIndexFileName, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(fileName, ArtifactSession.ArtifactIndexFileName, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(fileName, "luotsi-artifact-package.json", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(fileName, "session-timeline.jsonl", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(fileName, "session-replay.json", StringComparison.OrdinalIgnoreCase);

    private static string? ResolveDirectChildRoot(string baseRoot, string fullBase, string file)
    {
        var fullFile = Path.GetFullPath(file);
        if (!fullFile.StartsWith(fullBase + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !fullFile.StartsWith(fullBase + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var relative = Path.GetRelativePath(fullBase, fullFile);
        var firstSegment = relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(firstSegment) ? null : Path.Join(baseRoot, firstSegment);
    }

    private static string TrimDirectoryEnding(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
