using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;
using Luotsi.Cli.View.Session;

namespace Luotsi.Cli.Artifacts;

internal sealed class ArtifactCommandService(IFileSystem fileSystem, IArtifactFolderOpener artifactOpener, TimeProvider timeProvider)
{
    private const string MarkdownIndexFileName = ArtifactSession.ArtifactIndexFileName;
    private const string HtmlIndexFileName = ArtifactSession.ArtifactHtmlIndexFileName;
    private const string PackageManifestFileName = "luotsi-artifact-package.json";

    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly IArtifactFolderOpener _artifactOpener = artifactOpener ?? throw new ArgumentNullException(nameof(artifactOpener));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public async Task<ArtifactOpenResult> OpenAsync(string? target, string? searchRoot, bool dryRun, bool useLast)
    {
        var artifactRoot = useLast
            ? ArtifactRootResolver.ResolveLatestArtifactRoot(_fileSystem, searchRoot)
            : ArtifactRootResolver.ResolveArtifactRoot(_fileSystem, target!, searchRoot);
        var indexPath = await EnsureIndexAsync(artifactRoot).ConfigureAwait(false);
        if (!dryRun)
        {
            await _artifactOpener.OpenAsync(indexPath).ConfigureAwait(false);
        }

        var fileCount = GetPackableSourceFiles(artifactRoot).Length;
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

        var baseRoot = ArtifactRootResolver.ResolveSearchRoot(_fileSystem, searchRoot);
        if (!_fileSystem.DirectoryExists(baseRoot))
        {
            throw new UsageException($"Artifact search root '{baseRoot}' does not exist.");
        }

        var entries = ArtifactRootResolver.ResolveArtifactRootCandidates(_fileSystem, baseRoot)
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
                new ArtifactRecommendedCommandResult("open_latest_artifacts", "Open the latest artifact root from this search root.", $"luotsi artifacts open --last --artifacts {Quote(baseRoot)}"),
                new ArtifactRecommendedCommandResult("replay_open_latest", "Open the replay workbench for the latest artifact root from this search root.", $"luotsi replay open --last --artifacts {Quote(baseRoot)}"),
                new ArtifactRecommendedCommandResult("info_artifacts", "Inspect one artifact root or run id from this list without mutating it.", "luotsi artifacts info <artifact-root-or-run-id>"),
                new ArtifactRecommendedCommandResult("open_artifacts", "Open an artifact root or run id from this list.", "luotsi artifacts open <artifact-root-or-run-id>"),
                new ArtifactRecommendedCommandResult("pack_artifacts", "Pack an artifact root or run id from this list.", "luotsi artifacts pack <artifact-root-or-run-id>")
            ]));
    }

    public Task<ArtifactInfoResult> InfoAsync(string? target, string? searchRoot, bool useLast)
    {
        var artifactRoot = useLast
            ? ArtifactRootResolver.ResolveLatestArtifactRoot(_fileSystem, searchRoot)
            : ArtifactRootResolver.ResolveArtifactRoot(_fileSystem, target!, searchRoot);
        var files = GetArtifactFiles(artifactRoot);
        return Task.FromResult(new ArtifactInfoResult(
            Path.GetFileName(Path.GetFullPath(artifactRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            artifactRoot,
            files.Length,
            _fileSystem.FileExists(Path.Join(artifactRoot, HtmlIndexFileName)),
            _fileSystem.FileExists(Path.Join(artifactRoot, MarkdownIndexFileName)),
            _fileSystem.FileExists(Path.Join(artifactRoot, PackageManifestFileName)),
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
        var artifactRoot = ArtifactRootResolver.ResolveArtifactRoot(_fileSystem, target, searchRoot);
        var outputPath = ResolveOutputPath(artifactRoot, output);
        if (_fileSystem.FileExists(outputPath) && !force)
        {
            throw new UsageException($"Artifact pack output '{outputPath}' already exists. Use --force to overwrite it.");
        }

        var sourceFiles = GetPackableSourceFiles(artifactRoot, outputPath);
        var manifest = CreatePackageManifest(artifactRoot, sourceFiles);
        var entryCount = sourceFiles.Length + 1;
        if (dryRun)
        {
            return Task.FromResult(new ArtifactPackResult(
                artifactRoot,
                outputPath,
                entryCount,
                dryRun,
                PackageManifestFileName,
                manifest,
                null,
                [
                    new ArtifactRecommendedCommandResult("pack_artifacts", "Write this artifact package.", $"luotsi artifacts pack {Quote(artifactRoot)} --output {Quote(outputPath)}"),
                    new ArtifactRecommendedCommandResult("unpack_artifacts", "Restore this package locally after writing it.", $"luotsi artifacts unpack {Quote(outputPath)} --output {Quote(ResolveUnpackOutputPath(outputPath, null))}"),
                    new ArtifactRecommendedCommandResult("open_artifacts", "Open this artifact root in the local workbench.", $"luotsi artifacts open {Quote(artifactRoot)}")
                ]));
        }

        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            _fileSystem.CreateDirectory(outputDirectory);
        }

        PackArtifactRoot(artifactRoot, outputPath, sourceFiles, manifest, force);
        return Task.FromResult(new ArtifactPackResult(
            artifactRoot,
            outputPath,
            entryCount,
            dryRun,
            PackageManifestFileName,
            manifest,
            ComputeSha256(outputPath),
            [
                new ArtifactRecommendedCommandResult("unpack_artifacts", "Restore this package locally for review or replay.", $"luotsi artifacts unpack {Quote(outputPath)} --output {Quote(ResolveUnpackOutputPath(outputPath, null))}"),
                new ArtifactRecommendedCommandResult("open_artifacts", "Open this artifact root in the local workbench.", $"luotsi artifacts open {Quote(artifactRoot)}"),
                new ArtifactRecommendedCommandResult("replay_open", "Open the replay workbench for this artifact root.", $"luotsi replay open --artifacts {Quote(artifactRoot)}")
            ]));
    }

    public async Task<ArtifactUnpackResult> UnpackAsync(string packagePath, string? output, bool force, bool dryRun)
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

        var packageManifest = ReadPackageManifest(packagePath);
        var entries = ValidateArtifactPackage(packagePath, outputDirectory, force, packageManifest);
        var manifestOutputPath = Path.Join(outputDirectory, PackageManifestFileName);
        string? indexPath = null;
        if (!dryRun)
        {
            _fileSystem.CreateDirectory(outputDirectory);
            entries = UnpackArtifactPackage(packagePath, outputDirectory, force);
            indexPath = await RefreshUnpackedIndexAsync(outputDirectory).ConfigureAwait(false);
        }

        return new ArtifactUnpackResult(
            packagePath,
            outputDirectory,
            entries,
            dryRun,
            indexPath,
            PackageManifestFileName,
            manifestOutputPath,
            packageManifest,
            ComputeSha256(packagePath),
            [
                new ArtifactRecommendedCommandResult("info_artifacts", "Inspect the unpacked artifact root without mutating it.", $"luotsi artifacts info {Quote(outputDirectory)}"),
                new ArtifactRecommendedCommandResult("open_artifacts", "Open the unpacked artifact root.", $"luotsi artifacts open {Quote(outputDirectory)}"),
                new ArtifactRecommendedCommandResult("replay_open", "Open the replay workbench for the unpacked artifact root.", $"luotsi replay open --artifacts {Quote(outputDirectory)}")
            ]);
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

    private async Task<string> RefreshUnpackedIndexAsync(string artifactRoot)
    {
        var session = ArtifactSession.AttachExisting(artifactRoot, _fileSystem);
        await session.RefreshIndexAsync().ConfigureAwait(false);
        return Path.Join(artifactRoot, HtmlIndexFileName);
    }

    private void PackArtifactRoot(string artifactRoot, string outputPath, IReadOnlyList<string> sourceFiles, ArtifactPackageManifestResult manifest, bool force)
    {
        using var output = _fileSystem.OpenWrite(outputPath, overwrite: force);
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: false);
        WritePackageManifest(archive, manifest);
        foreach (var file in sourceFiles)
        {
            var entryName = NormalizeZipEntryName(Path.GetRelativePath(artifactRoot, file));
            var entry = archive.CreateEntry(entryName, CompressionLevel.SmallestSize);
            using var entryStream = entry.Open();
            using var input = _fileSystem.OpenRead(file);
            input.CopyTo(entryStream);
        }
    }

    private void WritePackageManifest(ZipArchive archive, ArtifactPackageManifestResult manifest)
    {
        var entry = archive.CreateEntry(PackageManifestFileName, CompressionLevel.SmallestSize);
        using var entryStream = entry.Open();
        JsonSerializer.Serialize(entryStream, manifest, ArtifactPackageJson.Options);
    }

    private ArtifactPackageManifestResult CreatePackageManifest(string artifactRoot, IReadOnlyList<string> sourceFiles)
    {
        var relativeFiles = sourceFiles
            .Select(path => NormalizeZipEntryName(Path.GetRelativePath(artifactRoot, path)))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ArtifactPackageManifestResult(
            ResultSchemas.ArtifactPackage,
            Path.GetFileName(Path.GetFullPath(artifactRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            _timeProvider.GetUtcNow(),
            relativeFiles.Length,
            CreateCategoryCounts(relativeFiles),
            [
                new ArtifactRecommendedCommandResult("info_artifacts", "Inspect the unpacked artifact root without opening it.", "luotsi artifacts info <unpacked-artifact-root>"),
                new ArtifactRecommendedCommandResult("open_artifacts", "Open the unpacked artifact root locally.", "luotsi artifacts open <unpacked-artifact-root>"),
                new ArtifactRecommendedCommandResult("replay_open", "Open the replay workbench for the unpacked artifact root.", "luotsi replay open --artifacts <unpacked-artifact-root>")
            ],
            relativeFiles);
    }

    private ArtifactPackageManifestResult ReadPackageManifest(string packagePath)
    {
        using var input = _fileSystem.OpenRead(packagePath);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: false);
        var entry = archive.Entries.FirstOrDefault(static entry =>
            string.Equals(entry.FullName.Replace('\\', '/'), PackageManifestFileName, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            throw new UsageException($"Artifact package '{packagePath}' is missing required manifest '{PackageManifestFileName}'.");
        }

        return ReadPackageManifest(entry.Open(), PackageManifestFileName);
    }

    private static ArtifactPackageManifestResult ReadPackageManifest(Stream stream, string manifestPath)
    {
        try
        {
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            var schema = GetRequiredString(root, "schema", manifestPath);
            if (!string.Equals(schema, ResultSchemas.ArtifactPackage, StringComparison.Ordinal))
            {
                throw new UsageException($"Artifact package manifest '{manifestPath}' has unsupported schema '{schema}'. Expected '{ResultSchemas.ArtifactPackage}'.");
            }

            var runId = GetRequiredString(root, "run_id", manifestPath);
            var createdAt = GetRequiredDateTimeOffset(root, "created_at", manifestPath);
            var sourceFileCount = GetRequiredInt(root, "source_file_count", manifestPath);
            if (!root.TryGetProperty("category_counts", out var categoryCountsElement) || categoryCountsElement.ValueKind != JsonValueKind.Object)
            {
                throw new UsageException($"Artifact package manifest '{manifestPath}' is missing object property 'category_counts'.");
            }

            if (!root.TryGetProperty("recommended_commands", out var recommendedCommandsElement) || recommendedCommandsElement.ValueKind != JsonValueKind.Array)
            {
                throw new UsageException($"Artifact package manifest '{manifestPath}' is missing array property 'recommended_commands'.");
            }

            if (!root.TryGetProperty("files", out var filesElement) || filesElement.ValueKind != JsonValueKind.Array)
            {
                throw new UsageException($"Artifact package manifest '{manifestPath}' is missing array property 'files'.");
            }

            var files = filesElement.EnumerateArray()
                .Select((element, index) =>
                {
                    if (element.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(element.GetString()))
                    {
                        throw new UsageException($"Artifact package manifest '{manifestPath}' has invalid files[{index}] entry.");
                    }

                    return element.GetString()!;
                })
                .ToArray();

            if (sourceFileCount != files.Length)
            {
                throw new UsageException($"Artifact package manifest '{manifestPath}' has source_file_count={sourceFileCount}, but files contains {files.Length} entries.");
            }

            files = ValidateManifestFiles(files, manifestPath);
            var commands = recommendedCommandsElement.EnumerateArray()
                .Select((element, index) =>
                {
                    if (element.ValueKind != JsonValueKind.Object)
                    {
                        throw new UsageException($"Artifact package manifest '{manifestPath}' has invalid recommended_commands[{index}] entry.");
                    }

                    return new ArtifactRecommendedCommandResult(
                        GetRequiredString(element, "kind", $"{manifestPath} recommended_commands[{index}]"),
                        GetRequiredString(element, "summary", $"{manifestPath} recommended_commands[{index}]"),
                        GetRequiredString(element, "command", $"{manifestPath} recommended_commands[{index}]"));
                })
                .ToArray();

            return new ArtifactPackageManifestResult(
                schema,
                runId,
                createdAt,
                sourceFileCount,
                ReadCategoryCounts(categoryCountsElement),
                commands,
                files);
        }
        catch (JsonException ex)
        {
            throw new UsageException($"Artifact package manifest '{manifestPath}' is not valid JSON: {ex.Message}");
        }
    }

    private static string GetRequiredString(JsonElement root, string propertyName, string context)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new UsageException($"Artifact package manifest '{context}' is missing string property '{propertyName}'.");
        }

        return value.GetString()!;
    }

    private static DateTimeOffset GetRequiredDateTimeOffset(JsonElement root, string propertyName, string manifestPath)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String || !value.TryGetDateTimeOffset(out var parsed))
        {
            throw new UsageException($"Artifact package manifest '{manifestPath}' is missing RFC 3339 string property '{propertyName}'.");
        }

        return parsed;
    }

    private static int GetRequiredInt(JsonElement root, string propertyName, string manifestPath)
    {
        if (!root.TryGetProperty(propertyName, out var value) || !value.TryGetInt32(out var parsed))
        {
            throw new UsageException($"Artifact package manifest '{manifestPath}' is missing integer property '{propertyName}'.");
        }

        return parsed;
    }

    private static ArtifactCategoryCountsResult ReadCategoryCounts(JsonElement root) =>
        new(
            GetOptionalInt(root, "screenshots"),
            GetOptionalInt(root, "videos"),
            GetOptionalInt(root, "reports"),
            GetOptionalInt(root, "logs"),
            GetOptionalInt(root, "timelines"),
            GetOptionalInt(root, "other"));

    private static string[] ValidateManifestFiles(IReadOnlyList<string> files, string manifestPath)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalizedFiles = new string[files.Count];

        for (var index = 0; index < files.Count; index++)
        {
            string normalized;
            try
            {
                normalized = NormalizeZipEntryName(NormalizePackageEntryName(files[index]));
            }
            catch (UsageException)
            {
                throw new UsageException($"Artifact package manifest '{manifestPath}' has invalid files[{index}] entry '{files[index]}'.");
            }

            if (string.Equals(normalized, PackageManifestFileName, StringComparison.OrdinalIgnoreCase))
            {
                throw new UsageException($"Artifact package manifest '{manifestPath}' has invalid files[{index}] entry '{files[index]}'.");
            }

            if (!seen.Add(normalized))
            {
                throw new UsageException($"Artifact package manifest '{manifestPath}' has duplicate files[{index}] entry '{files[index]}'.");
            }

            normalizedFiles[index] = normalized;
        }

        return normalizedFiles;
    }
    private static int GetOptionalInt(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var parsed)
            ? parsed
            : 0;

    private int UnpackArtifactPackage(string packagePath, string outputDirectory, bool force)
    {
        var fullOutputDirectory = Path.GetFullPath(outputDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var count = 0;
        using var input = _fileSystem.OpenRead(packagePath);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: false);
        foreach (var entry in archive.Entries.Where(static entry => !string.IsNullOrWhiteSpace(entry.Name)))
        {
            var safeEntryName = NormalizePackageEntryName(entry.FullName);
            var destinationPath = ResolvePackageDestination(outputDirectory, fullOutputDirectory, safeEntryName, entry.FullName, force);
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

    private int ValidateArtifactPackage(string packagePath, string outputDirectory, bool force, ArtifactPackageManifestResult manifest)
    {
        var fullOutputDirectory = Path.GetFullPath(outputDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var count = 0;
        var manifestFiles = manifest.Files.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seenEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var input = _fileSystem.OpenRead(packagePath);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: false);
        foreach (var entry in archive.Entries.Where(static entry => !string.IsNullOrWhiteSpace(entry.Name)))
        {
            var safeEntryName = NormalizePackageEntryName(entry.FullName);
            _ = ResolvePackageDestination(outputDirectory, fullOutputDirectory, safeEntryName, entry.FullName, force);
            var manifestEntryName = NormalizeZipEntryName(safeEntryName);
            if (!seenEntries.Add(manifestEntryName))
            {
                throw new UsageException($"Artifact package entry '{entry.FullName}' is duplicated in the archive.");
            }

            if (!string.Equals(manifestEntryName, PackageManifestFileName, StringComparison.OrdinalIgnoreCase) &&
                !manifestFiles.Contains(manifestEntryName))
            {
                throw new UsageException($"Artifact package entry '{entry.FullName}' is not declared in manifest '{PackageManifestFileName}'.");
            }

            count++;
        }

        var missingManifestFiles = manifest.Files
            .Where(file => !seenEntries.Contains(file))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (missingManifestFiles.Length > 0)
        {
            throw new UsageException($"Artifact package manifest '{PackageManifestFileName}' declares files that are missing from the package: {string.Join(", ", missingManifestFiles)}.");
        }

        return count;
    }

    private static string NormalizePackageEntryName(string entryName)
    {
        var normalized = entryName
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(normalized) || Path.IsPathRooted(normalized))
        {
            throw new UsageException($"Artifact package entry '{entryName}' would write outside the output directory.");
        }

        var segments = normalized.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(static segment => segment is "." or ".."))
        {
            throw new UsageException($"Artifact package entry '{entryName}' would write outside the output directory.");
        }

        return Path.Join(segments);
    }

    private string ResolvePackageDestination(string outputDirectory, string fullOutputDirectory, string safeEntryName, string originalEntryName, bool force)
    {
        var destinationPath = Path.GetFullPath(Path.Join(outputDirectory, safeEntryName));
        if (!destinationPath.StartsWith(fullOutputDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !destinationPath.StartsWith(fullOutputDirectory + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new UsageException($"Artifact package entry '{originalEntryName}' would write outside the output directory.");
        }

        if (_fileSystem.FileExists(destinationPath) && !force)
        {
            throw new UsageException($"Artifact unpack destination '{destinationPath}' already exists. Use --force to overwrite it.");
        }

        return destinationPath;
    }

    private string[] GetArtifactFiles(string artifactRoot) =>
        _fileSystem.GetFiles(artifactRoot, "*", SearchOption.AllDirectories)
            .OrderBy(path => Path.GetRelativePath(artifactRoot, path), StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private string[] GetPackableSourceFiles(string artifactRoot, string? outputPath = null) =>
        GetArtifactFiles(artifactRoot)
            .Where(path => !string.Equals(Path.GetFileName(path), PackageManifestFileName, StringComparison.OrdinalIgnoreCase))
            .Where(path => string.IsNullOrWhiteSpace(outputPath) || !string.Equals(Path.GetFullPath(path), Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
            .ToArray();

    private ArtifactListEntryResult CreateListEntry(string artifactRoot)
    {
        var files = GetArtifactFiles(artifactRoot);
        return new ArtifactListEntryResult(
            Path.GetFileName(Path.GetFullPath(artifactRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            artifactRoot,
            files.Length,
            _fileSystem.FileExists(Path.Join(artifactRoot, HtmlIndexFileName)),
            _fileSystem.FileExists(Path.Join(artifactRoot, MarkdownIndexFileName)),
            _fileSystem.FileExists(Path.Join(artifactRoot, PackageManifestFileName)),
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
    bool HasPackageManifest,
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
    bool HasPackageManifest,
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
    string? ManifestPath,
    ArtifactPackageManifestResult Manifest,
    string? Sha256,
    IReadOnlyList<ArtifactRecommendedCommandResult> RecommendedCommands);

internal sealed record ArtifactUnpackResult(
    string Package,
    string OutputDirectory,
    int EntryCount,
    bool DryRun,
    string? IndexPath,
    string ManifestPath,
    string ManifestOutputPath,
    ArtifactPackageManifestResult Manifest,
    string Sha256,
    IReadOnlyList<ArtifactRecommendedCommandResult> RecommendedCommands);

internal sealed record ArtifactRecommendedCommandResult(string Kind, string Summary, string Command);

internal sealed record ArtifactPackageManifestResult(
    string Schema,
    string RunId,
    DateTimeOffset CreatedAt,
    int SourceFileCount,
    ArtifactCategoryCountsResult CategoryCounts,
    IReadOnlyList<ArtifactRecommendedCommandResult> RecommendedCommands,
    IReadOnlyList<string> Files);

internal static class ArtifactPackageJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };
}
