using System.Net;
using System.Text;
using System.Text.Json;
using Luotsi.Cli.Infrastructure.Contracts;

namespace Luotsi.Cli.Artifacts;

internal sealed class ArtifactIndexRenderer(string root, IFileSystem fileSystem)
{
    private readonly string _root = root ?? throw new ArgumentNullException(nameof(root));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    public string BuildMarkdownIndex(IReadOnlyList<string> files)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Luotsi Artifacts");
        builder.AppendLine();
        builder.AppendLine($"Artifact root: `{_root}`");
        builder.AppendLine();
        if (files.Count == 0)
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

    public string BuildHtmlIndex(IReadOnlyList<string> files)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<!doctype html>");
        builder.AppendLine("<html lang=\"en\">");
        builder.AppendLine("<head>");
        builder.AppendLine("  <meta charset=\"utf-8\">");
        builder.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        builder.AppendLine("  <title>Luotsi Artifacts</title>");
        builder.AppendLine("  <style>");
        builder.AppendLine("    :root { color-scheme: light dark; --bg: #0f172a; --panel: #111827; --text: #e5e7eb; --muted: #9ca3af; --line: #334155; --accent: #38bdf8; }");
        builder.AppendLine("    @media (prefers-color-scheme: light) { :root { --bg: #f8fafc; --panel: #ffffff; --text: #0f172a; --muted: #475569; --line: #cbd5e1; --accent: #0369a1; } }");
        builder.AppendLine("    body { margin: 0; font: 14px/1.45 system-ui, -apple-system, Segoe UI, sans-serif; background: var(--bg); color: var(--text); }");
        builder.AppendLine("    main { max-width: 1040px; margin: 0 auto; padding: 32px 20px 48px; }");
        builder.AppendLine("    header { margin-bottom: 24px; }");
        builder.AppendLine("    h1 { margin: 0 0 8px; font-size: 28px; letter-spacing: 0; }");
        builder.AppendLine("    .root { color: var(--muted); word-break: break-all; }");
        builder.AppendLine("    section { margin-top: 22px; border: 1px solid var(--line); background: var(--panel); border-radius: 8px; overflow: hidden; }");
        builder.AppendLine("    h2 { margin: 0; padding: 14px 16px; font-size: 16px; border-bottom: 1px solid var(--line); }");
        builder.AppendLine("    ul { list-style: none; margin: 0; padding: 0; }");
        builder.AppendLine("    li { display: grid; grid-template-columns: minmax(0, 1fr) auto; gap: 16px; padding: 12px 16px; border-top: 1px solid var(--line); }");
        builder.AppendLine("    li:first-child { border-top: 0; }");
        builder.AppendLine("    a { color: var(--accent); text-decoration: none; overflow-wrap: anywhere; }");
        builder.AppendLine("    a:hover { text-decoration: underline; }");
        builder.AppendLine("    .kind { color: var(--muted); font-size: 12px; text-transform: uppercase; letter-spacing: .06em; }");
        builder.AppendLine("    .empty { padding: 18px 16px; color: var(--muted); }");
        builder.AppendLine("  </style>");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        builder.AppendLine("  <main>");
        builder.AppendLine("    <header>");
        builder.AppendLine("      <h1>Luotsi Artifacts</h1>");
        builder.AppendLine($"      <div class=\"root\">{HtmlEncode(_root)}</div>");
        builder.AppendLine("    </header>");

        if (files.Count == 0)
        {
            builder.AppendLine("    <section><h2>Artifacts</h2><div class=\"empty\">No artifacts have been written yet.</div></section>");
        }
        else
        {
            foreach (var group in files.GroupBy(GetArtifactCategory))
            {
                builder.AppendLine("    <section>");
                builder.AppendLine($"      <h2>{HtmlEncode(group.Key)}</h2>");
                builder.AppendLine("      <ul>");
                foreach (var file in group)
                {
                    builder.AppendLine("        <li>");
                    builder.AppendLine("          <div>");
                    builder.AppendLine($"            <a href=\"{HtmlAttributeEncode(EscapeHtmlLink(file))}\">{HtmlEncode(file)}</a>");
                    var summary = TryBuildArtifactSummary(file);
                    if (!string.IsNullOrWhiteSpace(summary))
                    {
                        builder.AppendLine($"            <div class=\"root\">{HtmlEncode(summary)}</div>");
                    }

                    builder.AppendLine("          </div>");
                    builder.AppendLine($"          <span class=\"kind\">{HtmlEncode(GetArtifactKind(file))}</span>");
                    builder.AppendLine("        </li>");
                }

                builder.AppendLine("      </ul>");
                builder.AppendLine("    </section>");
            }
        }

        builder.AppendLine("  </main>");
        builder.AppendLine("</body>");
        builder.AppendLine("</html>");
        return builder.ToString();
    }

    public static int GetArtifactSortGroup(string path) =>
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

    private string? TryBuildArtifactSummary(string path)
    {
        if (string.Equals(Path.GetExtension(path), ".jsonl", StringComparison.OrdinalIgnoreCase))
        {
            return TryBuildJsonlSummary(path);
        }

        return string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase)
            ? TryBuildJsonReportSummary(path)
            : null;
    }

    private string? TryBuildJsonReportSummary(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(_fileSystem.ReadAllTextAsync(Path.Join(_root, path)).GetAwaiter().GetResult());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!root.TryGetProperty("schema", out var schema) ||
                !string.Equals(schema.GetString(), "luotsi-scenario-run-report.v1", StringComparison.Ordinal))
            {
                return null;
            }

            var parts = new List<string>();
            AddJsonProperty(parts, root, "status");
            AddJsonProperty(parts, root, "total");
            AddJsonProperty(parts, root, "passed");
            AddJsonProperty(parts, root, "failed");
            AddJsonProperty(parts, root, "skipped");
            AddJsonProperty(parts, root, "durationMs", "duration_ms");
            return parts.Count == 0 ? null : string.Join(" | ", parts);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private string? TryBuildJsonlSummary(string path)
    {
        try
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var terminalStatuses = new List<string>();
            foreach (var (type, status) in _fileSystem
                .ReadAllTextAsync(Path.Join(_root, path))
                .GetAwaiter()
                .GetResult()
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(ParseJsonlEvent))
            {
                if (string.IsNullOrWhiteSpace(type))
                {
                    continue;
                }

                counts[type] = counts.GetValueOrDefault(type) + 1;
                if (status is not null)
                {
                    terminalStatuses.Add(status);
                }
            }

            if (counts.Count == 0)
            {
                return null;
            }

            var parts = new List<string> { $"events={counts.Values.Sum()}" };
            if (terminalStatuses.Count > 0)
            {
                parts.Add($"terminal={string.Join(",", terminalStatuses)}");
            }

            foreach (var (type, count) in counts.OrderBy(static item => item.Key, StringComparer.OrdinalIgnoreCase).Take(5))
            {
                parts.Add($"{type}={count}");
            }

            return string.Join(" | ", parts);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static (string? Type, string? Status) ParseJsonlEvent(string line)
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return (null, null);
        }

        if (!root.TryGetProperty("type", out var typeProperty))
        {
            return (null, null);
        }

        var type = typeProperty.GetString();
        var status = string.Equals(type, "scenario_run_ended", StringComparison.OrdinalIgnoreCase) &&
            root.TryGetProperty("status", out var statusProperty)
                ? statusProperty.GetString() ?? "unknown"
                : null;
        return (type, status);
    }

    private static void AddJsonProperty(List<string> parts, JsonElement root, string name, string? label = null)
    {
        if (!root.TryGetProperty(name, out var property))
        {
            return;
        }

        var value = property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add($"{label ?? ToSnakeCase(name)}={value}");
        }
    }

    private static string ToSnakeCase(string value)
    {
        var builder = new StringBuilder(value.Length + 4);
        foreach (var ch in value)
        {
            if (char.IsUpper(ch) && builder.Length > 0)
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(ch));
        }

        return builder.ToString();
    }

    private static string GetArtifactKind(string path)
    {
        var extension = Path.GetExtension(path);
        return string.IsNullOrWhiteSpace(extension) ? "file" : extension.TrimStart('.').ToUpperInvariant();
    }

    private static string EscapeMarkdownLink(string path) =>
        path.Replace("\\", "/", StringComparison.Ordinal).Replace(" ", "%20", StringComparison.Ordinal);

    private static string EscapeHtmlLink(string path) =>
        Uri.EscapeDataString(path.Replace("\\", "/", StringComparison.Ordinal)).Replace("%2F", "/", StringComparison.Ordinal);

    private static string HtmlEncode(string value) => WebUtility.HtmlEncode(value);

    private static string HtmlAttributeEncode(string value) => WebUtility.HtmlEncode(value);
}
