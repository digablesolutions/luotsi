namespace Luotsi.Cli.Artifacts;

internal sealed partial class ArtifactIndexRenderer
{
    public static int GetArtifactSortGroup(string path) =>
        GetArtifactCategory(path) switch
        {
            "Screenshots" => 0,
            "Recordings" => 1,
            "Replay" => 2,
            "Reports" => 3,
            "Logs" => 4,
            "Screen State" => 5,
            "Hierarchy" => 6,
            _ => 7
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

        if (fileName.Contains("replay", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("scenario-draft", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, SessionReplayArtifacts.TimelineFileName, StringComparison.OrdinalIgnoreCase))
        {
            return "Replay";
        }

        if (fileName.Contains("session-replay", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("session-timeline", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, FailureCapsuleArtifactNames.FileName, StringComparison.OrdinalIgnoreCase))
        {
            return "Reports";
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


    private static string GetArtifactKind(string path)
    {
        var extension = Path.GetExtension(path);
        return string.IsNullOrWhiteSpace(extension) ? "file" : extension.TrimStart('.').ToUpperInvariant();
    }
}
