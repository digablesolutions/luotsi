using System.Text;
using System.Text.Json;
using System.Net;
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
    private const string ArtifactHtmlIndexFileName = "index.html";

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
        await using var stream = _fileSystem.OpenWrite(path);
        await JsonSerializer.SerializeAsync(stream, value, value.GetType(), AppJson.Options).ConfigureAwait(false);
        await RefreshIndexAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Refreshes the Markdown index for artifacts written outside text/JSON helpers.
    /// </summary>
    public async Task RefreshIndexAsync()
    {
        var files = GetIndexedFiles();
        await _fileSystem.WriteAllTextAsync(GetArtifactPath(ArtifactIndexFileName), BuildMarkdownIndex(files), Encoding.UTF8).ConfigureAwait(false);
        await _fileSystem.WriteAllTextAsync(GetArtifactPath(ArtifactHtmlIndexFileName), BuildHtmlIndex(files), Encoding.UTF8).ConfigureAwait(false);
    }

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

    private string[] GetIndexedFiles() =>
        _fileSystem.GetFiles(Root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(Root, path))
            .Where(static path => !string.Equals(path, ArtifactIndexFileName, StringComparison.OrdinalIgnoreCase))
            .Where(static path => !string.Equals(path, ArtifactHtmlIndexFileName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(GetArtifactSortGroup)
            .ThenBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private string BuildMarkdownIndex(IReadOnlyList<string> files)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Luotsi Artifacts");
        builder.AppendLine();
        builder.AppendLine($"Artifact root: `{Root}`");
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

    private string BuildHtmlIndex(IReadOnlyList<string> files)
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
        builder.AppendLine($"      <div class=\"root\">{HtmlEncode(Root)}</div>");
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

    private static string GetArtifactKind(string path)
    {
        var extension = Path.GetExtension(path);
        return string.IsNullOrWhiteSpace(extension) ? "file" : extension.TrimStart('.').ToUpperInvariant();
    }

    private string? TryBuildArtifactSummary(string path)
    {
        if (string.Equals(Path.GetExtension(path), ".jsonl", StringComparison.OrdinalIgnoreCase))
        {
            return TryBuildJsonlSummary(path);
        }

        if (!string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(_fileSystem.ReadAllTextAsync(Path.Join(Root, path)).GetAwaiter().GetResult());
            var root = document.RootElement;
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
        catch
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
            foreach (var line in _fileSystem.ReadAllTextAsync(Path.Join(Root, path)).GetAwaiter().GetResult().Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (!root.TryGetProperty("type", out var typeProperty))
                {
                    continue;
                }

                var type = typeProperty.GetString();
                if (string.IsNullOrWhiteSpace(type))
                {
                    continue;
                }

                counts[type] = counts.GetValueOrDefault(type) + 1;
                if (string.Equals(type, "scenario_run_ended", StringComparison.OrdinalIgnoreCase) &&
                    root.TryGetProperty("status", out var status))
                {
                    terminalStatuses.Add(status.GetString() ?? "unknown");
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
        catch
        {
            return null;
        }
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

    private static string EscapeMarkdownLink(string path) =>
        path.Replace("\\", "/", StringComparison.Ordinal).Replace(" ", "%20", StringComparison.Ordinal);

    private static string EscapeHtmlLink(string path) =>
        Uri.EscapeDataString(path.Replace("\\", "/", StringComparison.Ordinal)).Replace("%2F", "/", StringComparison.Ordinal);

    private static string HtmlEncode(string value) => WebUtility.HtmlEncode(value);

    private static string HtmlAttributeEncode(string value) => WebUtility.HtmlEncode(value);

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
