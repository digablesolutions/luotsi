using Luotsi.Cli.Infrastructure;

namespace Luotsi.Cli.View;

/// <summary>
/// Resolves host-local candidate paths for bundled view assets and FFmpeg dependencies.
/// </summary>
public sealed class ViewHostPathResolver(IEnvironmentVariables environment)
{
    private const string FfmpegRootEnvironmentVariable = "LUOTSI_FFMPEG_ROOT";

    private readonly IEnvironmentVariables _environment = environment ?? throw new ArgumentNullException(nameof(environment));

    /// <summary>
    /// Enumerates repository-relative file candidates rooted at the current working directory and test/build output.
    /// </summary>
    /// <param name="relativePath">Repository-relative path to probe.</param>
    /// <returns>Candidate host-local file paths.</returns>
    public IEnumerable<string> GetRepositoryRelativeFileCandidates(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (Path.IsPathRooted(relativePath))
        {
            throw new ArgumentException("Path must be repository-relative.", nameof(relativePath));
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in new[]
                 {
                     Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), relativePath)),
                     Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", relativePath))
                 }.Where(seen.Add))
        {
            yield return candidate;
        }
    }

    /// <summary>
    /// Enumerates candidate ffmpeg executable paths.
    /// </summary>
    /// <returns>Candidate executable paths.</returns>
    public IEnumerable<string> GetFfmpegExecutablePathCandidates()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var executableName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";

        foreach (var directory in GetFfmpegDirectoryCandidates(includePathEntries: true))
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            var candidate = Path.GetFullPath(Path.Join(directory, executableName));
            if (seen.Add(candidate))
            {
                yield return candidate;
            }
        }
    }

    /// <summary>
    /// Enumerates candidate root directories for FFmpeg native libraries.
    /// </summary>
    /// <returns>Candidate library roots, plus a final <see langword="null"/> process-path fallback.</returns>
    public IEnumerable<string?> GetFfmpegLibraryRootCandidates()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in GetFfmpegDirectoryCandidates(includePathEntries: false))
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            var candidate = Path.GetFullPath(directory);
            if (seen.Add(candidate))
            {
                yield return candidate;
            }
        }

        var appBaseDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        if (seen.Add(appBaseDirectory))
        {
            yield return appBaseDirectory;
        }

        yield return null;
    }

    private IEnumerable<string> GetFfmpegDirectoryCandidates(bool includePathEntries)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in GetConfiguredFfmpegDirectories().Where(seen.Add))
        {
            yield return candidate;
        }

        foreach (var candidate in GetProcessRelativeDirectoryCandidates(Path.Combine("ffmpeg", "bin")).Where(seen.Add))
        {
            yield return candidate;
        }

        foreach (var candidate in GetProcessRelativeDirectoryCandidates("ffmpeg").Where(seen.Add))
        {
            yield return candidate;
        }

        if (!includePathEntries)
        {
            yield break;
        }

        var path = _environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            yield break;
        }

        foreach (var entry in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(seen.Add))
        {
            yield return entry;
        }
    }

    private IEnumerable<string> GetConfiguredFfmpegDirectories()
    {
        var configuredRoot = _environment.GetEnvironmentVariable(FfmpegRootEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(configuredRoot))
        {
            yield break;
        }

        var normalizedRoot = Path.GetFullPath(configuredRoot);
        yield return normalizedRoot;

        if (!IsBinDirectory(normalizedRoot))
        {
            yield return Path.Combine(normalizedRoot, "bin");
        }
    }

    private static IEnumerable<string> GetProcessRelativeDirectoryCandidates(string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            throw new ArgumentException("Path must be process-relative.", nameof(relativePath));
        }

        yield return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), relativePath));
        yield return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, relativePath));
    }

    private static bool IsBinDirectory(string path) =>
        string.Equals(new DirectoryInfo(path).Name, "bin", StringComparison.OrdinalIgnoreCase);
}
