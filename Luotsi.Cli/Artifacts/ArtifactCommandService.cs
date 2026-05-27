using System.IO.Compression;
using System.Security.Cryptography;
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

    public Task<ArtifactListResult> ListAsync(string? searchRoot, int limit)
    {
        if (limit <= 0)
        {
            throw new UsageException("Option --limit must be greater than zero.");
        }

        var baseRoot = ResolveSearchRoot(searchRoot);
        if (!_fileSystem.DirectoryExists(baseRoot))
        {
            throw new UsageException($"Artifact search root '{baseRoot}' does not exist.");
        }

        var entries = ResolveArtifactRootCandidates(baseRoot)
            .Select(root => CreateListEntry(root))
            .OrderByDescending(static entry => entry.RunId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entry => entry.ArtifactRoot, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToArray();

        return Task.FromResult(new ArtifactListResult(
            baseRoot,
            entries.Length,
            entries,
            [
                new ArtifactRecommendedCommandResult("info_artifacts", "Inspect one artifact root or run id from this list without mutating it.", "luotsi artifacts info <artifact-root-or-run-id>"),
                new ArtifactRecommendedCommandResult("open_artifacts", "Open an artifact root or run id from this list.", "luotsi artifacts open <artifact-root-or-run-id>"),
                new ArtifactRecommendedCommandResult("pack_artifacts", "Pack an artifact root or run id from this list.", "luotsi artifacts pack <artifact-root-or-run-id>")
            ]));
    }

    public Task<ArtifactInfoResult> InfoAsync(string target, string? searchRoot)
    {
        var artifactRoot = ResolveArtifactRoot(target, searchRoot);
        var files = GetArtifactFiles(artifactRoot);
        return Task.FromResult(new ArtifactInfoResult(
            Path.GetFileName(Path.GetFullPath(artifactRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            artifactRoot,
            files.Length,
            _fileSystem.FileExists(Path.Join(artifactRoot, HtmlIndexFileName)),
            _fileSystem.FileExists(Path.Join(artifactRoot, MarkdownIndexFileName)),
            files.Any(static file => string.Equals(Path.GetFileName(file), "session-timeline.jsonl", StringComparison.OrdinalIgnoreCase)),
            files.Any(static file => string.Equals(Path.GetFileName(file), "session-replay.json", StringComparison.OrdinalIgnoreCase)),
            CreateCategoryCounts(files),
            [
                new ArtifactRecommendedCommandResult("open_artifacts", "Open this artifact root in the local artifact browser.", $"luotsi artifacts open {Quote(artifactRoot)}"),
                new ArtifactRecommendedCommandResult("pack_artifacts", "Pack this artifact root for sharing or CI upload.", $"luotsi artifacts pack {Quote(artifactRoot)}"),
                new ArtifactRecommendedCommandResult("replay_open", "Open the replay workbench for this artifact root.", $"luotsi replay open --artifacts {Quote(artifactRoot)}")
            ]));
    }

    public Task<ArtifactPackResult> PackAsync(string target, string? searchRoot, string? output, bool force, bool dryRun)
    {
        var artifactRoot = ResolveArtifactRoot(target, searchRoot);
        var outputPath = ResolveOutputPath(artifactRoot, output);
        if (_fileSystem.FileExists(outputPath) && !force)
        {
            throw new UsageException($"Artifact pack output '{outputPath}' already exists. Use --force to overwrite it.");
        }

        var entries = CountPackableFiles(artifactRoot);
        if (dryRun)
        {
            return Task.FromResult(new ArtifactPackResult(
                artifactRoot,
                outputPath,
                entries,
                dryRun,
                null,
                [
                    new ArtifactRecommendedCommandResult("pack_artifacts", "Write this artifact package.", $"luotsi artifacts pack {Quote(artifactRoot)} --output {Quote(outputPath)}"),
                    new ArtifactRecommendedCommandResult("open_artifacts", "Open this artifact root in the local workbench.", $"luotsi artifacts open {Quote(artifactRoot)}")
                ]));
        }

        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            _fileSystem.CreateDirectory(outputDirectory);
        }

        entries = PackArtifactRoot(artifactRoot, outputPath, force);
        return Task.FromResult(new ArtifactPackResult(
            artifactRoot,
            outputPath,
            entries,
            dryRun,
            ComputeSha256(outputPath),
            [
                new ArtifactRecommendedCommandResult("open_artifacts", "Open this artifact root in the local workbench.", $"luotsi artifacts open {Quote(artifactRoot)}"),
                new ArtifactRecommendedCommandResult("replay_open", "Open the replay workbench for this artifact root.", $"luotsi replay open --artifacts {Quote(artifactRoot)}")
            ]));
    }

    public Task<ArtifactUnpackResult> UnpackAsync(string packagePath, string? output, bool force, bool dryRun)
    {
        if (string.IsNullOrWhiteSpace(packagePath))
        {
            throw new UsageException("artifacts unpack requires <artifact-zip>.");
        }

        if (!_fileSystem.FileExists(packagePath))
        {
            throw new UsageException($"Artifact package '{packagePath}' does not exist.");
        }

        var outputDirectory = ResolveUnpackOutputPath(packagePath, output);
        if (_fileSystem.DirectoryExists(outputDirectory) && !force)
        {
            throw new UsageException($"Artifact unpack output '{outputDirectory}' already exists. Use --force to write into it.");
        }

        var entries = ValidateArtifactPackage(packagePath, outputDirectory, force);
        if (!dryRun)
        {
            _fileSystem.CreateDirectory(outputDirectory);
            entries = UnpackArtifactPackage(packagePath, outputDirectory, force);
        }

        return Task.FromResult(new ArtifactUnpackResult(
            packagePath,
            outputDirectory,
            entries,
            dryRun,
            ComputeSha256(packagePath),
            [
                new ArtifactRecommendedCommandResult("open_artifacts", "Open the unpacked artifact root.", $"luotsi artifacts open {Quote(outputDirectory)}"),
                new ArtifactRecommendedCommandResult("replay_open", "Open the replay workbench for the unpacked artifact root.", $"luotsi replay open --artifacts {Quote(outputDirectory)}")
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

    private int UnpackArtifactPackage(string packagePath, string outputDirectory, bool force)
    {
        var fullOutputDirectory = Path.GetFullPath(outputDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var count = 0;
        using var input = _fileSystem.OpenRead(packagePath);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: false);
        foreach (var entry in archive.Entries.Where(static entry => !string.IsNullOrWhiteSpace(entry.Name)))
        {
            var destinationPath = ResolvePackageDestination(outputDirectory, fullOutputDirectory, entry.FullName, force);
            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                _fileSystem.CreateDirectory(destinationDirectory);
            }

            using var entryStream = entry.Open();
            using var output = _fileSystem.OpenWrite(destinationPath, overwrite: force);
            entryStream.CopyTo(output);
            count++;
        }

        return count;
    }

    private int ValidateArtifactPackage(string packagePath, string outputDirectory, bool force)
    {
        var fullOutputDirectory = Path.GetFullPath(outputDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var count = 0;
        using var input = _fileSystem.OpenRead(packagePath);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: false);
        foreach (var entry in archive.Entries.Where(static entry => !string.IsNullOrWhiteSpace(entry.Name)))
        {
            _ = ResolvePackageDestination(outputDirectory, fullOutputDirectory, entry.FullName, force);
            count++;
        }

        return count;
    }

    private string ResolvePackageDestination(string outputDirectory, string fullOutputDirectory, string entryName, bool force)
    {
        var destinationPath = Path.GetFullPath(Path.Join(outputDirectory, entryName));
        if (!destinationPath.StartsWith(fullOutputDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !destinationPath.StartsWith(fullOutputDirectory + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new UsageException($"Artifact package entry '{entryName}' would write outside the output directory.");
        }

        if (_fileSystem.FileExists(destinationPath) && !force)
        {
            throw new UsageException($"Artifact unpack destination '{destinationPath}' already exists. Use --force to overwrite it.");
        }

        return destinationPath;
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

        var baseRoot = ResolveSearchRoot(searchRoot);
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

    private ArtifactListEntryResult CreateListEntry(string artifactRoot)
    {
        var files = GetArtifactFiles(artifactRoot);
        return new ArtifactListEntryResult(
            Path.GetFileName(Path.GetFullPath(artifactRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            artifactRoot,
            files.Length,
            _fileSystem.FileExists(Path.Join(artifactRoot, HtmlIndexFileName)),
            _fileSystem.FileExists(Path.Join(artifactRoot, MarkdownIndexFileName)),
            files.Any(static file => string.Equals(Path.GetFileName(file), "session-timeline.jsonl", StringComparison.OrdinalIgnoreCase)),
            files.Any(static file => string.Equals(Path.GetFileName(file), "session-replay.json", StringComparison.OrdinalIgnoreCase)),
            $"luotsi artifacts info {Quote(artifactRoot)}",
            $"luotsi artifacts open {Quote(artifactRoot)}",
            $"luotsi artifacts pack {Quote(artifactRoot)}");
    }

    private static ArtifactCategoryCountsResult CreateCategoryCounts(IReadOnlyList<string> files)
    {
        var screenshots = 0;
        var videos = 0;
        var reports = 0;
        var logs = 0;
        var timelines = 0;

        foreach (var file in files)
        {
            var extension = Path.GetExtension(file).ToLowerInvariant();
            var name = Path.GetFileName(file);
            if (extension is ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp")
            {
                screenshots++;
            }
            else if (extension is ".mp4" or ".webm" or ".mov" or ".mkv")
            {
                videos++;
            }
            else if (extension is ".trx" || name.Equals("junit.xml", StringComparison.OrdinalIgnoreCase) || name.Contains("report", StringComparison.OrdinalIgnoreCase))
            {
                reports++;
            }
            else if (extension is ".log" or ".txt")
            {
                logs++;
            }
            else if (name.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase) || name.Contains("timeline", StringComparison.OrdinalIgnoreCase))
            {
                timelines++;
            }
        }

        return new ArtifactCategoryCountsResult(
            screenshots,
            videos,
            reports,
            logs,
            timelines,
            Math.Max(0, files.Count - screenshots - videos - reports - logs - timelines));
    }

    private string[] ResolveArtifactRootCandidates(string baseRoot)
    {
        var fullBase = Path.GetFullPath(baseRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var files = _fileSystem.GetFiles(baseRoot, "*", SearchOption.AllDirectories);
        if (files.Any(file => string.Equals(Path.GetDirectoryName(file)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), fullBase, StringComparison.OrdinalIgnoreCase)))
        {
            return [baseRoot];
        }

        return files
            .Select(file => ResolveDirectChildRoot(fullBase, file))
            .Where(static root => !string.IsNullOrWhiteSpace(root))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray()!;
    }

    private static string? ResolveDirectChildRoot(string fullBase, string file)
    {
        var fullFile = Path.GetFullPath(file);
        if (!fullFile.StartsWith(fullBase + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !fullFile.StartsWith(fullBase + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var relative = Path.GetRelativePath(fullBase, fullFile);
        var firstSegment = relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(firstSegment) ? null : Path.Join(fullBase, firstSegment);
    }

    private string ResolveSearchRoot(string? searchRoot) =>
        string.IsNullOrWhiteSpace(searchRoot)
            ? Path.Combine(_fileSystem.GetTempPath(), "luotsi")
            : searchRoot;

    private static string ResolveOutputPath(string artifactRoot, string? output)
    {
        if (!string.IsNullOrWhiteSpace(output))
        {
            return output;
        }

        var rootName = Path.GetFileName(Path.GetFullPath(artifactRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return Path.Join(Path.GetDirectoryName(Path.GetFullPath(artifactRoot)), $"{rootName}.zip");
    }

    private static string ResolveUnpackOutputPath(string packagePath, string? output)
    {
        if (!string.IsNullOrWhiteSpace(output))
        {
            return output;
        }

        var fullPath = Path.GetFullPath(packagePath);
        var directory = Path.GetDirectoryName(fullPath);
        var name = Path.GetFileNameWithoutExtension(fullPath);
        return Path.Join(directory, name);
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

    private string ComputeSha256(string path)
    {
        using var stream = _fileSystem.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}

internal sealed record ArtifactOpenResult(
    string ArtifactRoot,
    string IndexPath,
    bool DryRun,
    int FileCount,
    IReadOnlyList<ArtifactRecommendedCommandResult> RecommendedCommands);

internal sealed record ArtifactListResult(
    string SearchRoot,
    int Count,
    IReadOnlyList<ArtifactListEntryResult> Entries,
    IReadOnlyList<ArtifactRecommendedCommandResult> RecommendedCommands);

internal sealed record ArtifactListEntryResult(
    string RunId,
    string ArtifactRoot,
    int FileCount,
    bool HasHtmlIndex,
    bool HasMarkdownIndex,
    bool HasTimeline,
    bool HasReplayMetadata,
    string InfoCommand,
    string OpenCommand,
    string PackCommand);

internal sealed record ArtifactInfoResult(
    string RunId,
    string ArtifactRoot,
    int FileCount,
    bool HasHtmlIndex,
    bool HasMarkdownIndex,
    bool HasTimeline,
    bool HasReplayMetadata,
    ArtifactCategoryCountsResult CategoryCounts,
    IReadOnlyList<ArtifactRecommendedCommandResult> RecommendedCommands);

internal sealed record ArtifactCategoryCountsResult(
    int Screenshots,
    int Videos,
    int Reports,
    int Logs,
    int Timelines,
    int Other);

internal sealed record ArtifactPackResult(
    string ArtifactRoot,
    string Output,
    int EntryCount,
    bool DryRun,
    string? Sha256,
    IReadOnlyList<ArtifactRecommendedCommandResult> RecommendedCommands);

internal sealed record ArtifactUnpackResult(
    string Package,
    string OutputDirectory,
    int EntryCount,
    bool DryRun,
    string Sha256,
    IReadOnlyList<ArtifactRecommendedCommandResult> RecommendedCommands);

internal sealed record ArtifactRecommendedCommandResult(string Kind, string Summary, string Command);
