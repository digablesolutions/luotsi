using System.Text;
using System.Text.Json;
using VisitLab.Cli.Cli;
using VisitLab.Cli.Infrastructure;

namespace VisitLab.Cli.Artifacts;

/// <summary>
/// A per-command artifact session.
/// </summary>
public sealed class ArtifactSession
{
    private readonly IFileSystem _fileSystem;

    private ArtifactSession(string root, IFileSystem fileSystem)
    {
        Root = root;
        _fileSystem = fileSystem;
        _fileSystem.CreateDirectory(root);
    }

    /// <summary>
    /// Gets the artifact root path.
    /// </summary>
    public string Root { get; }

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
        var baseDir = options.Get("artifacts") ?? Path.Combine(activeFileSystem.GetTempPath(), "visit-lab");
        var name = $"{activeTimeProvider.GetUtcNow():yyyyMMdd-HHmmss}-{options.Command ?? "command"}";
        return new ArtifactSession(Path.Combine(baseDir, name), activeFileSystem);
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
    public object ToData() => new { artifact_root = Root };
}