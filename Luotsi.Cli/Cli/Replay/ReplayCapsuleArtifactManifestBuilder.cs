using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli.Replay;

internal static class ReplayCapsuleArtifactManifestBuilder
{
    public static IEnumerable<ReplayCapsuleArtifactManifestEntry> Build(IReadOnlyList<string> files) =>
        files
            .Select(static path => new ReplayCapsuleArtifactManifestEntry(
                path,
                GetArtifactKind(path),
                GetArtifactRole(path),
                GetArtifactSession(path)))
            .OrderBy(static entry => entry.Kind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entry => entry.Path, StringComparer.OrdinalIgnoreCase);

    private static string GetArtifactKind(string path)
    {
        var fileName = Path.GetFileName(path);
        if (fileName.Equals(SessionReplayArtifacts.TimelineFileName, StringComparison.OrdinalIgnoreCase))
        {
            return "timeline";
        }

        if (fileName.Equals(SessionReplayArtifacts.MetadataFileName, StringComparison.OrdinalIgnoreCase))
        {
            return "replay_metadata";
        }

        if (fileName.Equals(FailureCapsuleArtifactNames.FileName, StringComparison.OrdinalIgnoreCase))
        {
            return "failure_capsule";
        }

        if (fileName.Contains("scenario-draft", StringComparison.OrdinalIgnoreCase))
        {
            return "scenario_draft";
        }

        if (IsScreenshot(path))
        {
            return "screenshot";
        }

        if (IsVideo(path))
        {
            return "video";
        }

        if (IsLog(path))
        {
            return "log";
        }

        if (IsHierarchy(path))
        {
            return "hierarchy";
        }

        if (IsScreenState(path))
        {
            return "screen_state";
        }

        if (IsReport(path))
        {
            return "report";
        }

        return string.IsNullOrWhiteSpace(Path.GetExtension(path))
            ? "file"
            : Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
    }

    private static string GetArtifactRole(string path)
    {
        var fileName = Path.GetFileName(path);
        if (path.Contains("failure", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals(FailureCapsuleArtifactNames.FileName, StringComparison.OrdinalIgnoreCase))
        {
            return "failure";
        }

        if (fileName.Equals(SessionReplayArtifacts.TimelineFileName, StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals(SessionReplayArtifacts.MetadataFileName, StringComparison.OrdinalIgnoreCase))
        {
            return "session";
        }

        if (fileName.Contains("scenario-draft", StringComparison.OrdinalIgnoreCase))
        {
            return "scenario_authoring";
        }

        if (IsReport(path))
        {
            return "report";
        }

        return "supporting";
    }

    private static string? GetArtifactSession(string path)
    {
        var directory = Path.GetDirectoryName(path)?.Replace('\\', '/');
        return string.IsNullOrWhiteSpace(directory) || string.Equals(directory, ".", StringComparison.Ordinal)
            ? null
            : directory;
    }

    private static bool IsScreenshot(string path)
    {
        var fileName = Path.GetFileName(path);
        var extension = Path.GetExtension(path);
        return fileName.Contains("screenshot", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".png", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVideo(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".mkv", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".h264", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLog(string path)
    {
        var fileName = Path.GetFileName(path);
        var extension = Path.GetExtension(path);
        return fileName.Contains("logcat", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".log", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".txt", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHierarchy(string path)
    {
        var fileName = Path.GetFileName(path);
        return fileName.Contains("hierarchy", StringComparison.OrdinalIgnoreCase) ||
            Path.GetExtension(path).Equals(".xml", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsScreenState(string path) =>
        Path.GetFileName(path).Contains("screen-state", StringComparison.OrdinalIgnoreCase);

    private static bool IsReport(string path)
    {
        var fileName = Path.GetFileName(path);
        return fileName.Contains("report", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("junit.xml", StringComparison.OrdinalIgnoreCase);
    }
}
