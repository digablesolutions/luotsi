namespace Luotsi.Cli.Artifacts;

internal static class ArtifactIndexPaths
{
    public static string EscapeMarkdownLink(string path) =>
        path.Replace("\\", "/", StringComparison.Ordinal).Replace(" ", "%20", StringComparison.Ordinal);

    public static string EscapeHtmlLink(string path) =>
        Uri.EscapeDataString(path.Replace("\\", "/", StringComparison.Ordinal)).Replace("%2F", "/", StringComparison.Ordinal);

    public static string Quote(string value) =>
        value.Contains(' ', StringComparison.Ordinal) ? "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"" : value;

    public static string ResolveClusterRoot(string artifactRoot)
    {
        var parent = Path.GetDirectoryName(artifactRoot);
        return string.IsNullOrWhiteSpace(parent) ? artifactRoot : parent;
    }
}
