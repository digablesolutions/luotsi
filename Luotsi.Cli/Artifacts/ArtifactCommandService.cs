using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;
using Luotsi.Cli.View.Session;

namespace Luotsi.Cli.Artifacts;

internal sealed class ArtifactCommandService(IFileSystem fileSystem, IArtifactFolderOpener artifactOpener, TimeProvider timeProvider, IEnvironmentVariables environment)
{
    private const string MarkdownIndexFileName = ArtifactSession.ArtifactIndexFileName;
    private const string HtmlIndexFileName = ArtifactSession.ArtifactHtmlIndexFileName;
    private const string PackageManifestFileName = "luotsi-artifact-package.json";
    private const string IntakeSummaryFileName = "artifact-intake-summary.json";
    private const string IntakeReadmeFileName = "artifact-intake.md";
    private const string RedactionModeOff = "off";
    private const string RedactionModeLabSafe = "lab-safe";

    private static readonly Regex Sha256Pattern = new("^[A-Fa-f0-9]{64}$", RegexOptions.Compiled);

    private static readonly Regex QuotedSecretValuePattern = new(
        """(?i)(["']?)(token|secret|password|api[_-]?key|apikey)(["']?\s*[:=]\s*)(["'])([^"']*)(["'])""",
        RegexOptions.Compiled);

    private static readonly Regex UnquotedSecretValuePattern = new(
        """(?i)(["']?)(token|secret|password|api[_-]?key|apikey)(["']?\s*[:=]\s*)(?!bearer\b|basic\b)([^"'\s,;}&<]+)""",
        RegexOptions.Compiled);

    private static readonly Regex BearerTokenPattern = new(
        """(?i)\bbearer\s+[A-Za-z0-9._~+/=-]+""",
        RegexOptions.Compiled);

    private static readonly Regex BasicTokenPattern = new(
        """(?i)\bbasic\s+[A-Za-z0-9._~+/=-]+""",
        RegexOptions.Compiled);

    private static readonly Regex LongCredentialPattern = new(
        """\b(?:[A-Fa-f0-9]{32,}|[A-Za-z0-9+/]{40,}={0,2})\b""",
        RegexOptions.Compiled);

    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly IArtifactFolderOpener _artifactOpener = artifactOpener ?? throw new ArgumentNullException(nameof(artifactOpener));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly IEnvironmentVariables _environment = environment ?? throw new ArgumentNullException(nameof(environment));

    public async Task<ArtifactOpenResult> OpenAsync(string? target, string? searchRoot, bool dryRun, bool useLast)
    {
        var artifactRoot = useLast
            ? ArtifactRootResolver.ResolveLatestArtifactRoot(_fileSystem, searchRoot, _environment, preferWorkspaceHome: true)
            : ArtifactRootResolver.ResolveArtifactRoot(_fileSystem, target!, searchRoot, _environment, preferWorkspaceHome: true);
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
                new ArtifactRecommendedCommandResult("pack_artifacts_lab_safe", "Pack a lab-safe redacted copy for support, CI, or agents.", $"luotsi artifacts pack {Quote(artifactRoot)} --redact lab-safe"),
                new ArtifactRecommendedCommandResult("replay_open", "Open the replay workbench for this artifact root.", $"luotsi replay open --artifacts {Quote(artifactRoot)}")
            ]);
    }

    public Task<ArtifactListResult> ListAsync(string? searchRoot, int limit)
    {
        if (limit <= 0)
        {
            throw new UsageException("Option --limit must be greater than zero.");
        }

        var baseRoot = ArtifactRootResolver.ResolveSearchRoot(_fileSystem, searchRoot, _environment, preferWorkspaceHome: true);
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
                new ArtifactRecommendedCommandResult("pack_artifacts", "Pack an artifact root or run id from this list.", "luotsi artifacts pack <artifact-root-or-run-id>"),
                new ArtifactRecommendedCommandResult("pack_artifacts_lab_safe", "Pack a lab-safe redacted copy of an artifact root or run id.", "luotsi artifacts pack <artifact-root-or-run-id> --redact lab-safe")
            ]));
    }

    public async Task<object> InfoAsync(string? target, string? searchRoot, bool useLast)
    {
        if (!useLast && LooksLikePackageTarget(target) && !_fileSystem.DirectoryExists(target!))
        {
            if (_fileSystem.FileExists(target!))
            {
                return await CreatePackageInfoAsync(target!).ConfigureAwait(false);
            }

            if (IsExplicitPath(target!))
            {
                throw new UsageException($"Artifact package '{target}' does not exist.");
            }
        }

        var artifactRoot = useLast
            ? ArtifactRootResolver.ResolveLatestArtifactRoot(_fileSystem, searchRoot, _environment, preferWorkspaceHome: true)
            : ArtifactRootResolver.ResolveArtifactRoot(_fileSystem, target!, searchRoot, _environment, preferWorkspaceHome: true);
        var files = GetArtifactFiles(artifactRoot);
        var hasArtifactIntakeSummary = _fileSystem.FileExists(Path.Join(artifactRoot, IntakeSummaryFileName));
        var artifactIntakeSummary = await ReadArtifactInfoIntakeSummaryAsync(artifactRoot).ConfigureAwait(false);
        return new ArtifactInfoResult(
            Path.GetFileName(Path.GetFullPath(artifactRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            artifactRoot,
            files.Length,
            _fileSystem.FileExists(Path.Join(artifactRoot, HtmlIndexFileName)),
            _fileSystem.FileExists(Path.Join(artifactRoot, MarkdownIndexFileName)),
            _fileSystem.FileExists(Path.Join(artifactRoot, PackageManifestFileName)),
            files.Any(static file => string.Equals(Path.GetFileName(file), "session-timeline.jsonl", StringComparison.OrdinalIgnoreCase)),
            files.Any(static file => string.Equals(Path.GetFileName(file), "session-replay.json", StringComparison.OrdinalIgnoreCase)),
            hasArtifactIntakeSummary,
            artifactIntakeSummary,
            CreateCategoryCounts(files),
            [
                new ArtifactRecommendedCommandResult("open_artifacts", "Open this artifact root in the local artifact browser.", $"luotsi artifacts open {Quote(artifactRoot)}"),
                new ArtifactRecommendedCommandResult("pack_artifacts", "Pack this artifact root for sharing or CI upload.", $"luotsi artifacts pack {Quote(artifactRoot)}"),
                new ArtifactRecommendedCommandResult("pack_artifacts_lab_safe", "Pack a lab-safe redacted copy for support, CI, or agents.", $"luotsi artifacts pack {Quote(artifactRoot)} --redact lab-safe"),
                new ArtifactRecommendedCommandResult("replay_open", "Open the replay workbench for this artifact root.", $"luotsi replay open --artifacts {Quote(artifactRoot)}")
            ]);
    }

    private async Task<ArtifactInfoIntakeSummaryResult?> ReadArtifactInfoIntakeSummaryAsync(string artifactRoot)
    {
        var path = Path.Join(artifactRoot, IntakeSummaryFileName);
        if (!_fileSystem.FileExists(path))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(await _fileSystem.ReadAllTextAsync(path).ConfigureAwait(false));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            bool? shaVerified = null;
            if (root.TryGetProperty("verification", out var verification) &&
                verification.ValueKind == JsonValueKind.Object &&
                verification.TryGetProperty("verified", out var verified))
            {
                shaVerified = verified.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => null
                };
            }

            return new ArtifactInfoIntakeSummaryResult(
                TryGetOptionalString(root, "status"),
                TryGetOptionalString(root, "package"),
                GetOptionalInt(root, "entryCount"),
                TryGetOptionalString(root, "shareSafety"),
                GetOptionalBool(root, "labSafeRequired") ?? false,
                TryGetOptionalString(root, "sha256"),
                shaVerified,
                TryGetOptionalString(root, "jsonPath"),
                TryGetOptionalString(root, "readmePath"),
                CountOptionalArray(root, "recommendedCommands"));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public async Task<ArtifactPackResult> PackAsync(string target, string? searchRoot, string? output, bool force, bool dryRun, string? redactionMode)
    {
        var redactionPolicy = ParseRedactionMode(redactionMode);
        var artifactRoot = ArtifactRootResolver.ResolveArtifactRoot(_fileSystem, target, searchRoot, _environment, preferWorkspaceHome: true);
        var outputPath = ResolveOutputPath(artifactRoot, output);
        if (_fileSystem.FileExists(outputPath) && !force)
        {
            throw new UsageException($"Artifact pack output '{outputPath}' already exists. Use --force to overwrite it.");
        }

        var sourceFiles = GetPackableSourceFiles(artifactRoot, outputPath);
        var redaction = CreateRedactionSummary(sourceFiles, redactionPolicy);
        var manifest = CreatePackageManifest(artifactRoot, sourceFiles, redaction);
        var entryCount = sourceFiles.Length + 1;
        var labSafeOutputPath = ResolveLabSafeOutputPath(outputPath);
        if (dryRun)
        {
            return new ArtifactPackResult(
                artifactRoot,
                outputPath,
                entryCount,
                dryRun,
                PackageManifestFileName,
                manifest,
                null,
                [
                    new ArtifactRecommendedCommandResult("pack_artifacts", "Write this artifact package.", BuildPackCommand(artifactRoot, outputPath, redactionPolicy)),
                    new ArtifactRecommendedCommandResult("pack_artifacts_lab_safe", "Write a lab-safe redacted artifact package.", BuildPackCommand(artifactRoot, labSafeOutputPath, RedactionModeLabSafe)),
                    new ArtifactRecommendedCommandResult("unpack_artifacts", "Restore this package locally after writing it.", $"luotsi artifacts unpack {Quote(outputPath)} --output {Quote(ResolveUnpackOutputPath(outputPath, null))}"),
                    new ArtifactRecommendedCommandResult("open_artifacts", "Open this artifact root in the local workbench.", $"luotsi artifacts open {Quote(artifactRoot)}")
                ]);
        }

        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            _fileSystem.CreateDirectory(outputDirectory);
        }

        await PackArtifactRootAsync(artifactRoot, outputPath, sourceFiles, manifest, force, redactionPolicy).ConfigureAwait(false);
        var sha256 = await ComputeSha256Async(outputPath).ConfigureAwait(false);
        return new ArtifactPackResult(
            artifactRoot,
            outputPath,
            entryCount,
            dryRun,
            PackageManifestFileName,
            manifest,
            sha256,
            CreatePackRecommendedCommands(artifactRoot, outputPath, labSafeOutputPath, redactionPolicy, sha256));
    }

    public async Task<ArtifactVerifyResult> VerifyAsync(string packagePath, string? output, bool requireLabSafe, string? expectedSha256 = null)
    {
        if (string.IsNullOrWhiteSpace(packagePath))
        {
            throw new UsageException("artifacts verify requires <artifact.zip>.");
        }

        if (!_fileSystem.FileExists(packagePath))
        {
            throw new UsageException($"Artifact package '{packagePath}' does not exist.");
        }

        var normalizedExpectedSha256 = NormalizeExpectedSha256(expectedSha256);
        var outputDirectory = ResolveUnpackOutputPath(packagePath, output);
        var packageManifest = ReadPackageManifest(packagePath);
        var entries = ValidateArtifactPackage(packagePath, outputDirectory, force: true, packageManifest);
        var shareSafety = ResolveShareSafety(packageManifest);
        var blockers = ResolveVerifyBlockers(shareSafety, requireLabSafe);
        var status = blockers.Count > 0 ? "blocked" : "valid";
        var sha256 = await ComputeSha256Async(packagePath).ConfigureAwait(false);
        var verification = VerifyPackageSha256(packagePath, normalizedExpectedSha256, sha256);
        var unpackForce = _fileSystem.DirectoryExists(outputDirectory) ? " --force" : string.Empty;
        var unpackLabSafe = string.Equals(shareSafety, "lab_safe", StringComparison.Ordinal) ? " --require-lab-safe" : string.Empty;
        var unpackCommand = $"luotsi artifacts unpack {Quote(packagePath)} --output {Quote(outputDirectory)}{unpackForce}{unpackLabSafe} --sha256 {sha256}";
        return new ArtifactVerifyResult(
            packagePath,
            status,
            entries,
            outputDirectory,
            PackageManifestFileName,
            packageManifest,
            sha256,
            verification,
            shareSafety,
            requireLabSafe,
            blockers,
            CreateVerifyRecommendedCommands(packagePath, outputDirectory, unpackCommand, status));
    }

    public async Task<ArtifactUnpackResult> UnpackAsync(string packagePath, string? output, bool force, bool dryRun, bool requireLabSafe, string? expectedSha256 = null)
    {
        if (string.IsNullOrWhiteSpace(packagePath))
        {
            throw new UsageException("artifacts unpack requires <artifact-zip>.");
        }

        if (!_fileSystem.FileExists(packagePath))
        {
            throw new UsageException($"Artifact package '{packagePath}' does not exist.");
        }

        var normalizedExpectedSha256 = NormalizeExpectedSha256(expectedSha256);
        var outputDirectory = ResolveUnpackOutputPath(packagePath, output);
        if (_fileSystem.DirectoryExists(outputDirectory) && !force)
        {
            throw new UsageException($"Artifact unpack output '{outputDirectory}' already exists. Use --force to write into it.");
        }

        var packageManifest = ReadPackageManifest(packagePath);
        var shareSafety = ResolveShareSafety(packageManifest);
        var blockers = ResolveVerifyBlockers(shareSafety, requireLabSafe);
        if (blockers.Count > 0)
        {
            throw new UsageException($"Artifact package '{packagePath}' is not lab-safe. {blockers[0]}");
        }

        var sha256 = await ComputeSha256Async(packagePath).ConfigureAwait(false);
        var verification = VerifyPackageSha256(packagePath, normalizedExpectedSha256, sha256);
        var entries = ValidateArtifactPackage(packagePath, outputDirectory, force, packageManifest);
        var manifestOutputPath = Path.Join(outputDirectory, PackageManifestFileName);
        string? indexPath = null;
        if (!dryRun)
        {
            _fileSystem.CreateDirectory(outputDirectory);
            entries = await UnpackArtifactPackageAsync(packagePath, outputDirectory, force).ConfigureAwait(false);
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
            sha256,
            verification,
            [
                new ArtifactRecommendedCommandResult("info_artifacts", "Inspect the unpacked artifact root without mutating it.", $"luotsi artifacts info {Quote(outputDirectory)}"),
                new ArtifactRecommendedCommandResult("replay_packet_check", "Validate the restored run summary packet before triage.", BuildReplayPacketCheckCommand(outputDirectory)),
                new ArtifactRecommendedCommandResult("open_artifacts", "Open the unpacked artifact root.", $"luotsi artifacts open {Quote(outputDirectory)}"),
                new ArtifactRecommendedCommandResult("replay_open", "Open the replay workbench for the unpacked artifact root.", $"luotsi replay open --artifacts {Quote(outputDirectory)}"),
                CreateReplayCapsuleCommand("replay_capsule", "Write a replay capsule summary for handoff triage.", outputDirectory)
            ]);
    }

    public async Task<ArtifactIntakeResult> IntakeAsync(string packagePath, string? output, bool force, bool dryRun, bool requireLabSafe, bool open, bool writeJson, bool writeReadme, string? expectedSha256 = null)
    {
        if (open && dryRun)
        {
            throw new UsageException("artifacts intake --open cannot be combined with --dry-run.");
        }

        var unpack = await UnpackAsync(packagePath, output, force, dryRun, requireLabSafe, expectedSha256).ConfigureAwait(false);
        var shareSafety = ResolveShareSafety(unpack.Manifest);
        var status = dryRun ? "validated" : "restored";
        var jsonPath = writeJson && !dryRun ? Path.Join(unpack.OutputDirectory, IntakeSummaryFileName) : null;
        var readmePath = writeReadme && !dryRun ? Path.Join(unpack.OutputDirectory, IntakeReadmeFileName) : null;
        var result = new ArtifactIntakeResult(
            ResultSchemas.ArtifactIntake,
            unpack.Package,
            status,
            unpack.OutputDirectory,
            unpack.EntryCount,
            unpack.DryRun,
            open && !dryRun,
            unpack.IndexPath,
            unpack.ManifestPath,
            unpack.ManifestOutputPath,
            jsonPath,
            readmePath,
            unpack.Manifest,
            unpack.Sha256,
            shareSafety,
            requireLabSafe,
            unpack.Verification,
            CreateIntakeRecommendedCommands(unpack.Package, unpack.OutputDirectory, dryRun, open, requireLabSafe, writeJson, writeReadme, unpack.Sha256));

        if (!dryRun && (writeJson || writeReadme))
        {
            var artifacts = ArtifactSession.AttachExisting(unpack.OutputDirectory, _fileSystem);
            if (writeJson)
            {
                await artifacts.WriteJsonAsync(IntakeSummaryFileName, result).ConfigureAwait(false);
            }

            if (writeReadme)
            {
                await artifacts.WriteTextAsync(IntakeReadmeFileName, BuildIntakeReadme(result)).ConfigureAwait(false);
            }
        }

        if (open && !dryRun && unpack.IndexPath is not null)
        {
            await _artifactOpener.OpenAsync(unpack.IndexPath).ConfigureAwait(false);
        }

        return result;
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

    private async Task<ArtifactPackageInfoResult> CreatePackageInfoAsync(string packagePath)
    {
        var outputDirectory = ResolveUnpackOutputPath(packagePath, null);
        var packageManifest = ReadPackageManifest(packagePath);
        var entries = ValidateArtifactPackage(packagePath, outputDirectory, force: true, packageManifest);
        var sha256 = await ComputeSha256Async(packagePath).ConfigureAwait(false);
        var unpackForce = _fileSystem.DirectoryExists(outputDirectory) ? " --force" : string.Empty;
        var shareSafety = ResolveShareSafety(packageManifest);
        return new ArtifactPackageInfoResult(
            packagePath,
            packageManifest.RunId,
            entries,
            PackageManifestFileName,
            packageManifest,
            sha256,
            outputDirectory,
            CreatePackageInfoRecommendedCommands(packagePath, outputDirectory, unpackForce, shareSafety, sha256));
    }

    private async Task PackArtifactRootAsync(string artifactRoot, string outputPath, IReadOnlyList<string> sourceFiles, ArtifactPackageManifestResult manifest, bool force, string redactionMode)
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
            if (ShouldRedactFile(file, redactionMode))
            {
                await WriteRedactedTextAsync(input, entryStream).ConfigureAwait(false);
            }
            else
            {
                await input.CopyToAsync(entryStream).ConfigureAwait(false);
            }
        }
    }

    private void WritePackageManifest(ZipArchive archive, ArtifactPackageManifestResult manifest)
    {
        var entry = archive.CreateEntry(PackageManifestFileName, CompressionLevel.SmallestSize);
        using var entryStream = entry.Open();
        JsonSerializer.Serialize(entryStream, manifest, ArtifactPackageJson.Options);
    }

    private ArtifactPackageManifestResult CreatePackageManifest(string artifactRoot, IReadOnlyList<string> sourceFiles, ArtifactPackageRedactionResult? redaction)
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
                new ArtifactRecommendedCommandResult("replay_packet_check", "Validate the restored run summary packet before triage.", "luotsi replay packet --artifacts <unpacked-artifact-root> --check"),
                new ArtifactRecommendedCommandResult("open_artifacts", "Open the unpacked artifact root locally.", "luotsi artifacts open <unpacked-artifact-root>"),
                new ArtifactRecommendedCommandResult("replay_open", "Open the replay workbench for the unpacked artifact root.", "luotsi replay open --artifacts <unpacked-artifact-root>"),
                new ArtifactRecommendedCommandResult("replay_capsule", "Write a replay capsule summary for handoff triage.", "luotsi replay capsule --artifacts <unpacked-artifact-root> --write-json --write-readme")
            ],
            relativeFiles,
            redaction);
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
            var redaction = ReadOptionalRedaction(root, manifestPath);
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
                files,
                redaction);
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

    private static bool? GetOptionalBool(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static string? TryGetOptionalString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int CountOptionalArray(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.GetArrayLength()
            : 0;

    private static ArtifactPackageRedactionResult? ReadOptionalRedaction(JsonElement root, string manifestPath)
    {
        if (!root.TryGetProperty("redaction", out var redactionElement) || redactionElement.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (redactionElement.ValueKind != JsonValueKind.Object)
        {
            throw new UsageException($"Artifact package manifest '{manifestPath}' has invalid object property 'redaction'.");
        }

        var mode = GetRequiredString(redactionElement, "mode", $"{manifestPath} redaction");
        if (!string.Equals(mode, RedactionModeLabSafe, StringComparison.Ordinal) &&
            !string.Equals(mode, RedactionModeOff, StringComparison.Ordinal))
        {
            throw new UsageException($"Artifact package manifest '{manifestPath}' has unsupported redaction mode '{mode}'.");
        }

        return new ArtifactPackageRedactionResult(
            mode,
            GetRequiredInt(redactionElement, "redacted_file_count", $"{manifestPath} redaction"),
            GetRequiredInt(redactionElement, "text_file_count", $"{manifestPath} redaction"));
    }

    private async Task<int> UnpackArtifactPackageAsync(string packagePath, string outputDirectory, bool force)
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
            await entryStream.CopyToAsync(output).ConfigureAwait(false);
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

    private ArtifactPackageRedactionResult? CreateRedactionSummary(IReadOnlyList<string> sourceFiles, string redactionMode)
    {
        if (string.Equals(redactionMode, RedactionModeOff, StringComparison.Ordinal))
        {
            return null;
        }

        var textFileCount = 0;
        var redactedFileCount = 0;
        foreach (var file in sourceFiles)
        {
            if (!IsTextLikeArtifact(file))
            {
                continue;
            }

            textFileCount++;
            var text = ReadAllText(file);
            if (!string.Equals(text, RedactText(text), StringComparison.Ordinal))
            {
                redactedFileCount++;
            }
        }

        return new ArtifactPackageRedactionResult(redactionMode, redactedFileCount, textFileCount);
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

    private static string ParseRedactionMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return RedactionModeOff;
        }

        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            RedactionModeOff => RedactionModeOff,
            RedactionModeLabSafe => RedactionModeLabSafe,
            _ => throw new UsageException("Option --redact must be one of: off, lab-safe.")
        };
    }

    private static bool ShouldRedactFile(string path, string redactionMode) =>
        string.Equals(redactionMode, RedactionModeLabSafe, StringComparison.Ordinal) && IsTextLikeArtifact(path);

    private static bool LooksLikePackageTarget(string? target) =>
        !string.IsNullOrWhiteSpace(target) &&
        string.Equals(Path.GetExtension(target), ".zip", StringComparison.OrdinalIgnoreCase);

    private static bool IsExplicitPath(string target) =>
        Path.IsPathRooted(target) ||
        target.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
        target.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal);

    private static bool IsTextLikeArtifact(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (string.IsNullOrEmpty(extension) && string.Equals(Path.GetFileName(path), ".env", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return extension is ".json" or ".jsonl" or ".xml" or ".txt" or ".log" or ".md" or ".html" or ".csv" or ".properties" or ".env";
    }

    private string ReadAllText(string path)
    {
        using var input = _fileSystem.OpenRead(path);
        using var reader = new StreamReader(input, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static async Task WriteRedactedTextAsync(Stream input, Stream output)
    {
        using var buffer = new MemoryStream();
        await input.CopyToAsync(buffer).ConfigureAwait(false);
        var bytes = buffer.ToArray();

        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var original = await reader.ReadToEndAsync().ConfigureAwait(false);
        var redacted = RedactText(original);
        if (string.Equals(original, redacted, StringComparison.Ordinal))
        {
            await output.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            return;
        }

        await using var writer = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 1024, leaveOpen: true);
        await writer.WriteAsync(redacted).ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);
    }

    private static string RedactText(string text)
    {
        var redacted = QuotedSecretValuePattern.Replace(text, match =>
            $"{match.Groups[1].Value}{match.Groups[2].Value}{match.Groups[3].Value}{match.Groups[4].Value}[REDACTED:{NormalizeRedactionKind(match.Groups[2].Value)}]{match.Groups[6].Value}");
        redacted = UnquotedSecretValuePattern.Replace(redacted, match =>
            $"{match.Groups[1].Value}{match.Groups[2].Value}{match.Groups[3].Value}[REDACTED:{NormalizeRedactionKind(match.Groups[2].Value)}]");
        redacted = BearerTokenPattern.Replace(redacted, "Bearer [REDACTED:token]");
        redacted = BasicTokenPattern.Replace(redacted, "Basic [REDACTED:authorization]");
        redacted = LongCredentialPattern.Replace(redacted, "[REDACTED:credential]");
        return redacted;
    }

    private static string NormalizeRedactionKind(string key)
    {
        var normalized = key.Replace("_", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        return normalized switch
        {
            "apikey" => "apikey",
            "authorization" => "authorization",
            "password" => "password",
            "secret" => "secret",
            _ => "token"
        };
    }

    private static string BuildPackCommand(string artifactRoot, string outputPath, string redactionMode)
    {
        var command = $"luotsi artifacts pack {Quote(artifactRoot)} --output {Quote(outputPath)}";
        return string.Equals(redactionMode, RedactionModeLabSafe, StringComparison.Ordinal)
            ? $"{command} --redact lab-safe"
            : command;
    }

    private static string? NormalizeExpectedSha256(string? expectedSha256)
    {
        if (string.IsNullOrWhiteSpace(expectedSha256))
        {
            return null;
        }

        var normalizedExpected = expectedSha256.Trim().ToLowerInvariant();
        if (!Sha256Pattern.IsMatch(normalizedExpected))
        {
            throw new UsageException("Option --sha256 must be a 64-character hexadecimal SHA-256 digest.");
        }

        return normalizedExpected;
    }

    private static ArtifactPackageVerificationResult? VerifyPackageSha256(string packagePath, string? normalizedExpectedSha256, string actualSha256)
    {
        if (string.IsNullOrWhiteSpace(normalizedExpectedSha256))
        {
            return null;
        }

        if (!string.Equals(normalizedExpectedSha256, actualSha256, StringComparison.Ordinal))
        {
            throw new UsageException($"Artifact package '{packagePath}' SHA-256 mismatch. Expected {normalizedExpectedSha256}, actual {actualSha256}.");
        }

        return new ArtifactPackageVerificationResult("sha256", normalizedExpectedSha256, actualSha256, true);
    }

    private static IReadOnlyList<ArtifactRecommendedCommandResult> CreatePackRecommendedCommands(string artifactRoot, string outputPath, string labSafeOutputPath, string redactionMode, string sha256)
    {
        var unpackLabSafe = string.Equals(redactionMode, RedactionModeLabSafe, StringComparison.Ordinal)
            ? " --require-lab-safe"
            : string.Empty;
        var verifyCommand = $"luotsi artifacts verify {Quote(outputPath)} --sha256 {sha256}";
        if (string.Equals(redactionMode, RedactionModeLabSafe, StringComparison.Ordinal))
        {
            verifyCommand += " --require-lab-safe";
        }

        var commands = new List<ArtifactRecommendedCommandResult>
        {
            new("verify_artifacts", "Validate this package before handoff or restore.", verifyCommand),
            new("unpack_artifacts", "Restore this package locally after verifying its SHA-256.", $"luotsi artifacts unpack {Quote(outputPath)} --output {Quote(ResolveUnpackOutputPath(outputPath, null))}{unpackLabSafe} --sha256 {sha256}")
        };

        if (!string.Equals(redactionMode, RedactionModeLabSafe, StringComparison.Ordinal))
        {
            commands.Add(new ArtifactRecommendedCommandResult("pack_artifacts_lab_safe", "Create a lab-safe redacted copy of this artifact root.", BuildPackCommand(artifactRoot, labSafeOutputPath, RedactionModeLabSafe)));
        }

        commands.Add(new ArtifactRecommendedCommandResult("open_artifacts", "Open this artifact root in the local workbench.", $"luotsi artifacts open {Quote(artifactRoot)}"));
        commands.Add(new ArtifactRecommendedCommandResult("replay_open", "Open the replay workbench for this artifact root.", $"luotsi replay open --artifacts {Quote(artifactRoot)}"));
        return commands;
    }

    private static string ResolveShareSafety(ArtifactPackageManifestResult manifest) =>
        string.Equals(manifest.Redaction?.Mode, RedactionModeLabSafe, StringComparison.Ordinal)
            ? "lab_safe"
            : "not_redacted";

    private static IReadOnlyList<string> ResolveVerifyBlockers(string shareSafety, bool requireLabSafe)
    {
        if (!requireLabSafe || string.Equals(shareSafety, "lab_safe", StringComparison.Ordinal))
        {
            return [];
        }

        return ["Package was not packed with --redact lab-safe."];
    }

    private static IReadOnlyList<ArtifactRecommendedCommandResult> CreateVerifyRecommendedCommands(
        string packagePath,
        string outputDirectory,
        string unpackCommand,
        string status)
    {
        if (string.Equals(status, "blocked", StringComparison.Ordinal))
        {
            return
            [
                new("pack_artifacts_lab_safe", "Ask the sender to create a lab-safe redacted artifact package.", "luotsi artifacts pack <artifact-root-or-run-id> --output <artifact-lab-safe.zip> --redact lab-safe"),
                new("verify_artifacts_lab_safe", "Re-run the lab-safe handoff gate before unpacking.", $"luotsi artifacts verify {Quote(packagePath)} --require-lab-safe")
            ];
        }

        return
        [
            new ArtifactRecommendedCommandResult("unpack_artifacts", "Restore this verified package locally after verifying its SHA-256.", unpackCommand),
            new ArtifactRecommendedCommandResult("info_artifacts", "Inspect the restored artifact root after unpacking.", $"luotsi artifacts info {Quote(outputDirectory)}"),
            new ArtifactRecommendedCommandResult("replay_packet_check", "Validate the restored run summary packet before triage.", BuildReplayPacketCheckCommand(outputDirectory)),
            new ArtifactRecommendedCommandResult("replay_open", "Open the replay workbench after unpacking.", $"luotsi replay open --artifacts {Quote(outputDirectory)}")
        ];
    }

    private static IReadOnlyList<ArtifactRecommendedCommandResult> CreatePackageInfoRecommendedCommands(string packagePath, string outputDirectory, string unpackForce, string shareSafety, string sha256)
    {
        var labSafeGate = string.Equals(shareSafety, "lab_safe", StringComparison.Ordinal) ? " --require-lab-safe" : string.Empty;
        return
        [
            new("verify_artifacts", "Validate this package explicitly before handoff or restore.", $"luotsi artifacts verify {Quote(packagePath)} --output {Quote(outputDirectory)}{labSafeGate} --sha256 {sha256}"),
            new("unpack_artifacts", "Restore this validated package locally after verifying its SHA-256.", $"luotsi artifacts unpack {Quote(packagePath)} --output {Quote(outputDirectory)}{unpackForce}{labSafeGate} --sha256 {sha256}"),
            new("unpack_artifacts_dry_run", "Re-run package validation and SHA-256 verification without writing files.", $"luotsi artifacts unpack {Quote(packagePath)} --output {Quote(outputDirectory)}{unpackForce}{labSafeGate} --dry-run --sha256 {sha256}"),
            new("replay_packet_check_after_unpack", "Validate the restored run summary packet before triage.", BuildReplayPacketCheckCommand(outputDirectory)),
            new("open_artifacts_after_unpack", "Open the restored artifact root after unpacking.", $"luotsi artifacts open {Quote(outputDirectory)}"),
            new("replay_open_after_unpack", "Open the replay workbench after unpacking.", $"luotsi replay open --artifacts {Quote(outputDirectory)}"),
            CreateReplayCapsuleCommand("replay_capsule_after_unpack", "Write a replay capsule summary after unpacking.", outputDirectory)
        ];
    }

    private static IReadOnlyList<ArtifactRecommendedCommandResult> CreateIntakeRecommendedCommands(string packagePath, string outputDirectory, bool dryRun, bool opened, bool requireLabSafe, bool writeJson, bool writeReadme, string sha256)
    {
        var commands = new List<ArtifactRecommendedCommandResult>
        {
            new("info_artifacts", "Inspect the restored artifact root without mutating it.", $"luotsi artifacts info {Quote(outputDirectory)}"),
            new("replay_packet_check", "Validate the restored run summary packet before triage.", BuildReplayPacketCheckCommand(outputDirectory)),
            new("replay_open", "Open the replay workbench for the restored artifact root.", $"luotsi replay open --artifacts {Quote(outputDirectory)}")
        };

        if (dryRun)
        {
            var labSafeGate = requireLabSafe ? " --require-lab-safe" : string.Empty;
            var jsonFlag = writeJson ? " --write-json" : string.Empty;
            var readmeFlag = writeReadme ? " --write-readme" : string.Empty;
            commands.Insert(0, new ArtifactRecommendedCommandResult("intake_artifacts", "Restore this package after the dry-run validation.", $"luotsi artifacts intake {Quote(packagePath)} --output {Quote(outputDirectory)}{labSafeGate}{jsonFlag}{readmeFlag} --sha256 {sha256}"));
        }
        else if (!opened)
        {
            commands.Insert(1, new ArtifactRecommendedCommandResult("open_artifacts", "Open the restored artifact root.", $"luotsi artifacts open {Quote(outputDirectory)}"));
        }

        return commands;
    }

    private static ArtifactRecommendedCommandResult CreateReplayCapsuleCommand(string kind, string summary, string artifactRoot) =>
        new(kind, summary, $"luotsi replay capsule --artifacts {Quote(artifactRoot)} --write-json --write-readme");

    private static string BuildReplayPacketCheckCommand(string artifactRoot) =>
        $"luotsi replay packet --artifacts {Quote(artifactRoot)} --check";

    private static string BuildIntakeReadme(ArtifactIntakeResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Artifact Intake");
        builder.AppendLine();
        builder.AppendLine($"- Status: `{EscapeMarkdown(result.Status)}`");
        builder.AppendLine($"- Package: `{EscapeMarkdown(result.Package)}`");
        builder.AppendLine($"- Output: `{EscapeMarkdown(result.OutputDirectory)}`");
        builder.AppendLine($"- Share safety: `{EscapeMarkdown(result.ShareSafety)}`");
        builder.AppendLine($"- Lab-safe required: `{result.LabSafeRequired.ToString().ToLowerInvariant()}`");
        builder.AppendLine($"- SHA-256: `{EscapeMarkdown(result.Sha256)}`");
        if (result.Verification is not null)
        {
            builder.AppendLine($"- SHA verified: `{result.Verification.Verified.ToString().ToLowerInvariant()}`");
        }

        builder.AppendLine();
        builder.AppendLine("## Next Commands");
        foreach (var command in result.RecommendedCommands)
        {
            builder.AppendLine($"- **{EscapeMarkdown(command.Summary)}** (`{EscapeMarkdown(command.Kind)}`)");
            builder.AppendLine($"  `{EscapeMarkdown(command.Command)}`");
        }

        return builder.ToString();
    }

    private static string ResolveLabSafeOutputPath(string outputPath)
    {
        var separatorIndex = outputPath.LastIndexOfAny(['/', '\\']);
        var directoryPrefix = separatorIndex >= 0 ? outputPath[..(separatorIndex + 1)] : string.Empty;
        var fileName = separatorIndex >= 0 ? outputPath[(separatorIndex + 1)..] : outputPath;
        var extension = Path.GetExtension(fileName);
        var stem = string.IsNullOrEmpty(extension) ? fileName : fileName[..^extension.Length];
        var labSafeName = stem.EndsWith("-lab-safe", StringComparison.OrdinalIgnoreCase)
            ? fileName
            : $"{stem}-lab-safe{extension}";

        return directoryPrefix + labSafeName;
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

    private static string EscapeMarkdown(string value) =>
        value.Replace("`", "\\`", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);

    private async Task<string> ComputeSha256Async(string path)
    {
        using var stream = _fileSystem.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
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
    bool HasArtifactIntakeSummary,
    ArtifactInfoIntakeSummaryResult? ArtifactIntakeSummary,
    ArtifactCategoryCountsResult CategoryCounts,
    IReadOnlyList<ArtifactRecommendedCommandResult> RecommendedCommands);

internal sealed record ArtifactInfoIntakeSummaryResult(
    string? Status,
    string? Package,
    int EntryCount,
    string? ShareSafety,
    bool LabSafeRequired,
    string? Sha256,
    bool? ShaVerified,
    string? JsonPath,
    string? ReadmePath,
    int RecommendedCommandCount);

internal sealed record ArtifactPackageInfoResult(
    string Package,
    string RunId,
    int EntryCount,
    string ManifestPath,
    ArtifactPackageManifestResult Manifest,
    string Sha256,
    string DefaultOutputDirectory,
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

internal sealed record ArtifactVerifyResult(
    string Package,
    string Status,
    int EntryCount,
    string SuggestedOutputDirectory,
    string ManifestPath,
    ArtifactPackageManifestResult Manifest,
    string Sha256,
    ArtifactPackageVerificationResult? Verification,
    string ShareSafety,
    bool LabSafeRequired,
    IReadOnlyList<string> Blockers,
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
    ArtifactPackageVerificationResult? Verification,
    IReadOnlyList<ArtifactRecommendedCommandResult> RecommendedCommands);

internal sealed record ArtifactIntakeResult(
    string Schema,
    string Package,
    string Status,
    string OutputDirectory,
    int EntryCount,
    bool DryRun,
    bool Opened,
    string? IndexPath,
    string ManifestPath,
    string ManifestOutputPath,
    string? JsonPath,
    string? ReadmePath,
    ArtifactPackageManifestResult Manifest,
    string Sha256,
    string ShareSafety,
    bool LabSafeRequired,
    ArtifactPackageVerificationResult? Verification,
    IReadOnlyList<ArtifactRecommendedCommandResult> RecommendedCommands);

internal sealed record ArtifactRecommendedCommandResult(string Kind, string Summary, string Command);

internal sealed record ArtifactPackageVerificationResult(
    string Algorithm,
    string Expected,
    string Actual,
    bool Verified);

internal sealed record ArtifactPackageRedactionResult(
    string Mode,
    int RedactedFileCount,
    int TextFileCount);

internal sealed record ArtifactPackageManifestResult(
    string Schema,
    string RunId,
    DateTimeOffset CreatedAt,
    int SourceFileCount,
    ArtifactCategoryCountsResult CategoryCounts,
    IReadOnlyList<ArtifactRecommendedCommandResult> RecommendedCommands,
    IReadOnlyList<string> Files,
    ArtifactPackageRedactionResult? Redaction = null);

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
