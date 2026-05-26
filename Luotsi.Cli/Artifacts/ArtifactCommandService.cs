using System.IO.Compression;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.View.Session;

namespace Luotsi.Cli.Artifacts;

internal sealed class ArtifactCommandService(IFileSystem fileSystem, IArtifactFolderOpener artifactOpener)
{
    private const string MarkdownIndexFileName = ArtifactSession.ArtifactIndexFileName;
    private const string HtmlIndexFileName = ArtifactSession.ArtifactHtmlIndexFileName;

    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly IArtifactFolderOpener _artifactOpener = artifactOpener ?? throw new ArgumentNullException(nameof(artifactOpener));

    public async Task<ArtifactOpenResult> OpenAsync(string target, string? searchRoot, bool dryRun)
    {
        var artifactRoot = ResolveArtifactRoot(target, searchRoot);
        var indexPath = await EnsureIndexAsync(artifactRoot).ConfigureAwait(false);
        if (!dryRun)
        {
            await _artifactOpener.OpenAsync(indexPath).ConfigureAwait(false);
        }

        var fileCount = CountPackableFiles(artifactRoot);
        return new ArtifactOpenResult(
            artifactRoot,
            indexPath,
            dryRun,
            fileCount,
            [
                new ArtifactRecommendedCommandResult("pack_artifacts", "Pack this artifact root for sharing or CI upload.", $"luotsi artifacts pack {Quote(artifactRoot)}"),
                new ArtifactRecommendedCommandResult("replay_open", "Open the replay workbench for this artifact root.", $"luotsi replay open --artifacts {Quote(artifactRoot)}")
            ]);
    }

    public Task<ArtifactPackResult> PackAsync(string target, string? searchRoot, string? output, bool force)
    {
        var artifactRoot = ResolveArtifactRoot(target, searchRoot);
        var outputPath = ResolveOutputPath(artifactRoot, output);
        if (_fileSystem.FileExists(outputPath) && !force)
        {
            throw new UsageException($"Artifact pack output '{outputPath}' already exists. Use --force to overwrite it.");
        }

        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            _fileSystem.CreateDirectory(outputDirectory);
        }

        var entries = PackArtifactRoot(artifactRoot, outputPath, force);
        return Task.FromResult(new ArtifactPackResult(
            artifactRoot,
            outputPath,
            entries,
            [
                new ArtifactRecommendedCommandResult("open_artifacts", "Open this artifact root in the local workbench.", $"luotsi artifacts open {Quote(artifactRoot)}"),
                new ArtifactRecommendedCommandResult("replay_open", "Open the replay workbench for this artifact root.", $"luotsi replay open --artifacts {Quote(artifactRoot)}")
            ]));
    }

    private async Task<string> EnsureIndexAsync(string artifactRoot)
    {
        var htmlIndexPath = Path.Join(artifactRoot, HtmlIndexFileName);
        if (_fileSystem.FileExists(htmlIndexPath))
        {
            return htmlIndexPath;
        }

        var markdownIndexPath = Path.Join(artifactRoot, MarkdownIndexFileName);
        if (_fileSystem.FileExists(markdownIndexPath))
        {
            return markdownIndexPath;
        }

        var session = ArtifactSession.AttachExisting(artifactRoot, _fileSystem);
        await session.RefreshIndexAsync().ConfigureAwait(false);
        return htmlIndexPath;
    }

    private int PackArtifactRoot(string artifactRoot, string outputPath, bool force)
    {
        var files = GetArtifactFiles(artifactRoot)
            .Where(path => !string.Equals(Path.GetFullPath(path), Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
            .ToArray();

        using var output = _fileSystem.OpenWrite(outputPath, overwrite: force);
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: false);
        foreach (var file in files)
        {
            var entryName = NormalizeZipEntryName(Path.GetRelativePath(artifactRoot, file));
            var entry = archive.CreateEntry(entryName, CompressionLevel.SmallestSize);
            using var entryStream = entry.Open();
            using var input = _fileSystem.OpenRead(file);
            input.CopyTo(entryStream);
        }

        return files.Length;
    }

    private int CountPackableFiles(string artifactRoot) => GetArtifactFiles(artifactRoot).Length;

    private string[] GetArtifactFiles(string artifactRoot) =>
        _fileSystem.GetFiles(artifactRoot, "*", SearchOption.AllDirectories)
            .OrderBy(path => Path.GetRelativePath(artifactRoot, path), StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private string ResolveArtifactRoot(string target, string? searchRoot)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            throw new UsageException("Artifact command requires <artifact-root-or-run-id>.");
        }

        if (_fileSystem.DirectoryExists(target))
        {
            return target;
        }

        var baseRoot = string.IsNullOrWhiteSpace(searchRoot)
            ? Path.Combine(_fileSystem.GetTempPath(), "luotsi")
            : searchRoot;
        if (!_fileSystem.DirectoryExists(baseRoot))
        {
            throw new UsageException($"Artifact root '{target}' does not exist, and search root '{baseRoot}' does not exist.");
        }

        var matches = _fileSystem.GetFiles(baseRoot, "*", SearchOption.AllDirectories)
            .Select(path => FindAncestorByName(baseRoot, path, target))
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return matches.Length switch
        {
            1 => matches[0]!,
            0 => throw new UsageException($"Artifact root or run id '{target}' was not found under '{baseRoot}'."),
            _ => throw new UsageException($"Artifact run id '{target}' matched multiple roots under '{baseRoot}'. Use the full artifact root path.")
        };
    }

    private static string ResolveOutputPath(string artifactRoot, string? output)
    {
        if (!string.IsNullOrWhiteSpace(output))
        {
            return output;
        }

        var rootName = Path.GetFileName(Path.GetFullPath(artifactRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return Path.Join(Path.GetDirectoryName(Path.GetFullPath(artifactRoot)), $"{rootName}.zip");
    }

    private static string? FindAncestorByName(string baseRoot, string filePath, string name)
    {
        var current = Path.GetDirectoryName(filePath);
        var fullBase = Path.GetFullPath(baseRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (string.Equals(Path.GetFileName(current), name, StringComparison.OrdinalIgnoreCase))
            {
                return current;
            }

            var fullCurrent = Path.GetFullPath(current).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(fullCurrent, fullBase, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            current = Path.GetDirectoryName(current);
        }

        return null;
    }

    private static string NormalizeZipEntryName(string path) =>
        path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');

    private static string Quote(string value) =>
        value.Any(static ch => char.IsWhiteSpace(ch) || ch == '"')
            ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : value;
}

internal sealed record ArtifactOpenResult(
    string ArtifactRoot,
    string IndexPath,
    bool DryRun,
    int FileCount,
    IReadOnlyList<ArtifactRecommendedCommandResult> RecommendedCommands);

internal sealed record ArtifactPackResult(
    string ArtifactRoot,
    string Output,
    int EntryCount,
    IReadOnlyList<ArtifactRecommendedCommandResult> RecommendedCommands);

internal sealed record ArtifactRecommendedCommandResult(string Kind, string Summary, string Command);
