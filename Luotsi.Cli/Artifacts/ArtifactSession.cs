using System.Text;
using System.Text.Json;
using Luotsi.Cli.Cli;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Infrastructure.Serialization;
using Luotsi.Cli.Infrastructure.System;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Artifacts;

/// <summary>
/// A per-command artifact session.
/// </summary>
public sealed class ArtifactSession
{
    private readonly IFileSystem _fileSystem;
    private readonly ArtifactIndexRenderer _indexRenderer;
    internal const string ArtifactIndexFileName = "index.md";
    internal const string ArtifactHtmlIndexFileName = "index.html";

    private ArtifactSession(string root, IFileSystem fileSystem, UiPollArtifactPolicy uiPollArtifactPolicy, bool ensureDirectoryExists)
    {
        Root = root;
        _fileSystem = fileSystem;
        _indexRenderer = new ArtifactIndexRenderer(root, fileSystem);
        UiPollArtifactPolicy = uiPollArtifactPolicy;
        if (ensureDirectoryExists)
        {
            _fileSystem.CreateDirectory(root);
        }
        else if (!_fileSystem.DirectoryExists(root))
        {
            throw new UsageException($"Artifact root '{root}' does not exist.");
        }
    }

    /// <summary>
    /// Gets the artifact root path.
    /// </summary>
    public string Root { get; }

    /// <summary>
    /// Gets the artifact policy used for UI polling loops.
    /// </summary>
    public UiPollArtifactPolicy UiPollArtifactPolicy { get; }

    /// <summary>
    /// Creates an artifact session from CLI options.
    /// </summary>
    /// <param name="options">CLI options.</param>
    /// <param name="fileSystem"></param>
    /// <param name="timeProvider"></param>
    /// <returns>Artifact session.</returns>
    public static ArtifactSession Create(
        CliOptions options,
        IFileSystem? fileSystem = null,
        TimeProvider? timeProvider = null,
        IEnvironmentVariables? environment = null,
        bool preferWorkspaceHome = false)
    {
        var activeFileSystem = fileSystem ?? new PhysicalFileSystem();
        var activeTimeProvider = timeProvider ?? TimeProvider.System;
        var baseDir = ResolveBaseDirectory(options, activeFileSystem, environment, preferWorkspaceHome);
        var name = $"{activeTimeProvider.GetUtcNow():yyyyMMdd-HHmmss}-{SanitizePathSegment(options.Command)}";
        return new ArtifactSession(Path.Combine(baseDir, name), activeFileSystem, ParseUiPollArtifactPolicy(options.Get("poll-artifacts")), ensureDirectoryExists: true);
    }

    public static ArtifactSession AttachExisting(string root, IFileSystem? fileSystem = null, string? pollArtifacts = null)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new UsageException("Artifact root must be a non-empty directory path.");
        }

        var activeFileSystem = fileSystem ?? new PhysicalFileSystem();
        return new ArtifactSession(root, activeFileSystem, ParseUiPollArtifactPolicy(pollArtifacts), ensureDirectoryExists: false);
    }

    /// <summary>
    /// Writes a text artifact.
    /// </summary>
    /// <param name="name">File name.</param>
    /// <param name="text">Text content.</param>
    public async Task WriteTextAsync(string name, string text)
    {
        await _fileSystem.WriteAllTextAsync(GetArtifactPath(name), text, Encoding.UTF8).ConfigureAwait(false);
        await RefreshIndexAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Writes a JSON artifact.
    /// </summary>
    /// <param name="name">File name.</param>
    /// <param name="value">Value to serialize.</param>
    public async Task WriteJsonAsync(string name, object value)
    {
        var path = GetArtifactPath(name);
        await using (var stream = _fileSystem.OpenWrite(path))
        {
            await JsonSerializer.SerializeAsync(stream, value, value.GetType(), AppJson.Options).ConfigureAwait(false);
        }

        await RefreshIndexAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Refreshes the Markdown index for artifacts written outside text/JSON helpers.
    /// </summary>
    public async Task RefreshIndexAsync()
    {
        _ = await RefreshIndexWithSnapshotAsync().ConfigureAwait(false);
    }

    internal async Task<ArtifactIndexSnapshot> RefreshIndexWithSnapshotAsync()
    {
        var files = GetIndexedFiles();
        var replaySummaries = new SessionReplaySummaryReader(Root, _fileSystem).ReadSummaries(files);
        var snapshot = new ArtifactIndexSnapshot(files, replaySummaries);
        var markdownIndex = await _indexRenderer.BuildMarkdownIndexAsync(files, replaySummaries).ConfigureAwait(false);
        await _fileSystem.WriteAllTextAsync(GetArtifactPath(ArtifactIndexFileName), markdownIndex, Encoding.UTF8).ConfigureAwait(false);
        var htmlIndex = await _indexRenderer.BuildHtmlIndexAsync(files, replaySummaries).ConfigureAwait(false);
        await _fileSystem.WriteAllTextAsync(GetArtifactPath(ArtifactHtmlIndexFileName), htmlIndex, Encoding.UTF8).ConfigureAwait(false);
        return snapshot;
    }

    /// <summary>
    /// Returns JSON envelope artifact data.
    /// </summary>
    /// <returns>Artifact metadata.</returns>
    public ArtifactData ToData() => new(Root, ToOptionValue(UiPollArtifactPolicy));

    internal Stream OpenArtifactWrite(string name, bool overwrite = true) =>
        _fileSystem.OpenWrite(GetArtifactPath(name), overwrite);

    private string GetArtifactPath(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new UsageException("Artifact name must be a non-empty file name.");
        }

        if (Path.IsPathRooted(name) || !string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal))
        {
            throw new UsageException("Artifact name must be a file name without directory segments.");
        }

        return Path.Join(Root, name);
    }

    private string[] GetIndexedFiles() =>
        _fileSystem.GetFiles(Root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(Root, path))
            .Where(static path => !string.Equals(path, ArtifactIndexFileName, StringComparison.OrdinalIgnoreCase))
            .Where(static path => !string.Equals(path, ArtifactHtmlIndexFileName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(ArtifactIndexRenderer.GetArtifactSortGroup)
            .ThenBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static UiPollArtifactPolicy ParseUiPollArtifactPolicy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return UiPollArtifactPolicy.Final;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "final" => UiPollArtifactPolicy.Final,
            "per-attempt" or "perattempt" => UiPollArtifactPolicy.PerAttempt,
            "none" => UiPollArtifactPolicy.None,
            _ => throw new UsageException("Option --poll-artifacts must be one of: final, per-attempt, none.")
        };
    }

    private static string ResolveBaseDirectory(CliOptions options, IFileSystem fileSystem, IEnvironmentVariables? environment, bool preferWorkspaceHome)
    {
        var artifacts = options.Get("artifacts");
        var outputDir = options.Get("output-dir");
        if (!string.IsNullOrWhiteSpace(artifacts) &&
            !string.IsNullOrWhiteSpace(outputDir) &&
            !string.Equals(Path.GetFullPath(artifacts), Path.GetFullPath(outputDir), StringComparison.OrdinalIgnoreCase))
        {
            throw new UsageException("Use either --artifacts or --output-dir for the artifact root, not both.");
        }

        if (!string.IsNullOrWhiteSpace(artifacts) || !string.IsNullOrWhiteSpace(outputDir))
        {
            return artifacts ?? outputDir!;
        }

        return preferWorkspaceHome
            ? ArtifactWorkspacePaths.ResolveDefaultRunArtifactBaseDirectory(fileSystem, environment)
            : Path.Join(fileSystem.GetTempPath(), "luotsi");
    }

    private static string SanitizePathSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "command";
        }

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (ch == Path.DirectorySeparatorChar || ch == Path.AltDirectorySeparatorChar || Array.IndexOf(Path.GetInvalidFileNameChars(), ch) >= 0)
            {
                builder.Append('-');
                continue;
            }

            builder.Append(ch);
        }

        var sanitized = builder.ToString().Trim('-', '.');
        return string.IsNullOrWhiteSpace(sanitized) ? "command" : sanitized;
    }

    private static string ToOptionValue(UiPollArtifactPolicy policy) =>
        policy switch
        {
            UiPollArtifactPolicy.Final => "final",
            UiPollArtifactPolicy.PerAttempt => "per-attempt",
            UiPollArtifactPolicy.None => "none",
            _ => throw new InvalidOperationException($"Unsupported poll artifact policy '{policy}'.")
        };
}

internal sealed record ArtifactIndexSnapshot(
    IReadOnlyList<string> Files,
    IReadOnlyList<SessionReplaySummary> ReplaySummaries);
