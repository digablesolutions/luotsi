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
    public Task WriteTextAsync(string name, string text) => _fileSystem.WriteAllTextAsync(Path.Combine(Root, name), text, Encoding.UTF8);

    /// <summary>
    /// Writes a JSON artifact.
    /// </summary>
    /// <param name="name">File name.</param>
    /// <param name="value">Value to serialize.</param>
    public Task WriteJsonAsync(string name, object value) => WriteTextAsync(name, JsonSerializer.Serialize(value, AppJson.Options));

    /// <summary>
    /// Returns JSON envelope artifact data.
    /// </summary>
    /// <returns>Artifact metadata.</returns>
    public ArtifactData ToData() => new(Root, ToOptionValue(UiPollArtifactPolicy));

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