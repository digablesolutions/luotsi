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
    private const string ArtifactIndexFileName = "index.md";

    private ArtifactSession(string root, IFileSystem fileSystem, UiPollArtifactPolicy uiPollArtifactPolicy)
    {
        Root = root;
        _fileSystem = fileSystem;
        UiPollArtifactPolicy = uiPollArtifactPolicy;
        _fileSystem.CreateDirectory(root);
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
    public static ArtifactSession Create(CliOptions options, IFileSystem? fileSystem = null, TimeProvider? timeProvider = null)
    {
        var activeFileSystem = fileSystem ?? new PhysicalFileSystem();
        var activeTimeProvider = timeProvider ?? TimeProvider.System;
        var baseDir = options.Get("artifacts") ?? Path.Combine(activeFileSystem.GetTempPath(), "luotsi");
        var name = $"{activeTimeProvider.GetUtcNow():yyyyMMdd-HHmmss}-{options.Command ?? "command"}";
        return new ArtifactSession(Path.Combine(baseDir, name), activeFileSystem, ParseUiPollArtifactPolicy(options.Get("poll-artifacts")));
    }

    /// <summary>
    /// Writes a text artifact.
    /// </summary>
    /// <param name="name">File name.</param>
    /// <param name="text">Text content.</param>
    public async Task WriteTextAsync(string name, string text)
    {
        await _fileSystem.WriteAllTextAsync(GetArtifactPath(name), text, Encoding.UTF8).ConfigureAwait(false);
        await _fileSystem.WriteAllTextAsync(GetArtifactPath(ArtifactIndexFileName), BuildMarkdownIndex(), Encoding.UTF8).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes a JSON artifact.
    /// </summary>
    /// <param name="name">File name.</param>
    /// <param name="value">Value to serialize.</param>
    public async Task WriteJsonAsync(string name, object value)
    {
        var path = GetArtifactPath(name);
        await using var stream = _fileSystem.OpenWrite(path);
        await JsonSerializer.SerializeAsync(stream, value, value.GetType(), AppJson.Options).ConfigureAwait(false);
        await RefreshIndexAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Refreshes the Markdown index for artifacts written outside text/JSON helpers.
    /// </summary>
    public Task RefreshIndexAsync() =>
        _fileSystem.WriteAllTextAsync(GetArtifactPath(ArtifactIndexFileName), BuildMarkdownIndex(), Encoding.UTF8);

    /// <summary>
    /// Returns JSON envelope artifact data.
    /// </summary>
    /// <returns>Artifact metadata.</returns>
    public ArtifactData ToData() => new(Root, ToOptionValue(UiPollArtifactPolicy));

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

    private string BuildMarkdownIndex()
    {
        var files = _fileSystem.GetFiles(Root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(Root, path))
            .Where(static path => !string.Equals(path, ArtifactIndexFileName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(GetArtifactSortGroup)
            .ThenBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var builder = new StringBuilder();
        builder.AppendLine("# Luotsi Artifacts");
        builder.AppendLine();
        builder.AppendLine($"Artifact root: `{Root}`");
        builder.AppendLine();
        if (files.Length == 0)
        {
            builder.AppendLine("No artifacts have been written yet.");
            return builder.ToString();
        }

        foreach (var group in files.GroupBy(GetArtifactCategory))
        {
            builder.AppendLine($"## {group.Key}");
            builder.AppendLine();
            foreach (var file in group)
            {
                builder.AppendLine($"- [{file}]({EscapeMarkdownLink(file)})");
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static int GetArtifactSortGroup(string path) =>
        GetArtifactCategory(path) switch
        {
            "Screenshots" => 0,
            "Recordings" => 1,
            "Reports" => 2,
            "Logs" => 3,
            "Screen State" => 4,
            "Hierarchy" => 5,
            _ => 6
        };

    private static string GetArtifactCategory(string path)
    {
        var extension = Path.GetExtension(path);
        var fileName = Path.GetFileName(path);
        if (string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase))
        {
            return "Screenshots";
        }

        if (string.Equals(extension, ".mp4", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".webm", StringComparison.OrdinalIgnoreCase))
        {
            return "Recordings";
        }

        if (fileName.Contains("report", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("junit", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".trx", StringComparison.OrdinalIgnoreCase))
        {
            return "Reports";
        }

        if (string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("logcat", StringComparison.OrdinalIgnoreCase))
        {
            return "Logs";
        }

        if (fileName.Contains("screen-state", StringComparison.OrdinalIgnoreCase))
        {
            return "Screen State";
        }

        if (fileName.Contains("hierarchy", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".xml", StringComparison.OrdinalIgnoreCase))
        {
            return "Hierarchy";
        }

        return "Other";
    }

    private static string EscapeMarkdownLink(string path) =>
        path.Replace("\\", "/", StringComparison.Ordinal).Replace(" ", "%20", StringComparison.Ordinal);

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

    private static string ToOptionValue(UiPollArtifactPolicy policy) =>
        policy switch
        {
            UiPollArtifactPolicy.Final => "final",
            UiPollArtifactPolicy.PerAttempt => "per-attempt",
            UiPollArtifactPolicy.None => "none",
            _ => throw new InvalidOperationException($"Unsupported poll artifact policy '{policy}'.")
        };
}
