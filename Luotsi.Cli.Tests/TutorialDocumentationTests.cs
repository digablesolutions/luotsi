using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Luotsi.Cli.Cli;
using Luotsi.Cli.Scenarios;
using Xunit;

namespace Luotsi.Cli.Tests;

public sealed partial class AppTests
{
    [Fact]
    public void Tutorial_Documentation_Links_Resolve()
    {
        var docsRoot = Path.GetFullPath(Path.Join(FindRepositoryRoot(), "docs"));
        var markdownFiles = Directory.GetFiles(docsRoot, "*.md", SearchOption.AllDirectories);
        var missingLinks = FindMissingLinks(markdownFiles, docsRoot);

        Assert.Empty(missingLinks);
    }

    [Fact]
    public void Website_Documentation_Links_Resolve()
    {
        var websiteDocsRoot = Path.GetFullPath(Path.Join(FindRepositoryRoot(), "website", "src", "content", "docs", "docs"));
        var contentFiles = Directory.GetFiles(websiteDocsRoot, "*.*", SearchOption.AllDirectories)
            .Where(static path => string.Equals(Path.GetExtension(path), ".md", StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetExtension(path), ".mdx", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var missingLinks = FindMissingLinks(contentFiles, websiteDocsRoot);

        Assert.Empty(missingLinks);
    }

    [Fact]
    public void Website_Documentation_Sidebar_Slugs_Resolve()
    {
        var root = FindRepositoryRoot();
        var websiteDocsRoot = Path.GetFullPath(Path.Join(root, "website", "src", "content", "docs", "docs"));
        var astroConfigPath = Path.Join(root, "website", "astro.config.mjs");
        var astroConfig = File.ReadAllText(astroConfigPath);
        var missingSlugs = new List<string>();

        foreach (var slug in SidebarSlugRegex()
                     .Matches(astroConfig)
                     .Cast<Match>()
                     .Select(static match => match.Groups["target"].Value.Trim())
                     .Where(static slug => !string.IsNullOrWhiteSpace(slug)))
        {
            if (!ResolveWebsiteSidebarSlugTargets(websiteDocsRoot, slug).Any(TargetExists))
            {
                missingSlugs.Add(slug);
            }
        }

        Assert.Empty(missingSlugs);
    }

    [Fact]
    public void Command_Reference_Documents_Known_Command_And_Help_Surfaces()
    {
        var markdown = File.ReadAllText(Path.Join(FindRepositoryRoot(), "docs", "commands.md"));
        var documentedCommandPaths = ExtractDocumentedCommandPaths(markdown);
        var missingTopLevelCommands = CliOptions.KnownCommandNames
            .Where(command => !IsTopLevelCommandDocumented(documentedCommandPaths, command))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var missingHelpCommands = ExtractHelpCommandPaths()
            .Where(commandPath => !IsCommandPathDocumented(documentedCommandPaths, commandPath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Empty(missingTopLevelCommands);
        Assert.Empty(missingHelpCommands);
    }

    [Fact]
    public void Command_Reference_Documents_Artifact_Package_Handoff_Strings()
    {
        var markdown = File.ReadAllText(Path.Join(FindRepositoryRoot(), "docs", "commands.md"));

        Assert.Contains("--output-dir <directory>", markdown, StringComparison.Ordinal);
        Assert.Contains("luotsi-artifact-package.json", markdown, StringComparison.Ordinal);
        Assert.Contains("artifacts pack", markdown, StringComparison.Ordinal);
        Assert.Contains("artifacts unpack", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Artifact_Package_Manifest_Fixture_Parses_And_Passes_Unpack_Validation()
    {
        var manifestPath = Path.Join(FindRepositoryRoot(), "Luotsi.Cli.Tests", "Fixtures", "artifacts", "package-manifest-v1.json");
        var manifestJson = File.ReadAllText(manifestPath);
        using var fixture = JsonDocument.Parse(manifestJson);
        Assert.Equal("luotsi-artifact-package.v1", fixture.RootElement.GetProperty("schema").GetString());
        Assert.Equal("20260526-120000-run", fixture.RootElement.GetProperty("run_id").GetString());
        Assert.Equal(2, fixture.RootElement.GetProperty("source_file_count").GetInt32());
        Assert.Equal(2, fixture.RootElement.GetProperty("files").GetArrayLength());

        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var packagePath = "/tmp/share/fixture.zip";
        fileSystem.CreateDirectory("/tmp/share");
        await using (var packageStream = fileSystem.OpenWrite(packagePath))
        {
            using var archive = new ZipArchive(packageStream, ZipArchiveMode.Create, leaveOpen: true);
            var index = archive.CreateEntry("index.html");
            await using (var entry = index.Open())
            await using (var writer = new StreamWriter(entry))
            {
                await writer.WriteAsync("<!doctype html>");
            }

            var timeline = archive.CreateEntry("session-timeline.jsonl");
            await using (var entry = timeline.Open())
            await using (var writer = new StreamWriter(entry))
            {
                await writer.WriteAsync("{\"type\":\"session_started\"}");
            }

            var manifest = archive.CreateEntry("luotsi-artifact-package.json");
            await using (var entry = manifest.Open())
            await using (var writer = new StreamWriter(entry))
            {
                await writer.WriteAsync(manifestJson);
            }
        }

        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem
        });

        var exitCode = await app.RunAsync(["artifacts", "unpack", packagePath, "--output", "/tmp/unpacked", "--dry-run"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        Assert.Equal("20260526-120000-run", envelope.RootElement.GetProperty("data").GetProperty("manifest").GetProperty("run_id").GetString());
    }

    [Fact]
    public void Scenario_Playbooks_Document_All_Supported_Actions()
    {
        var markdown = File.ReadAllText(Path.Join(FindRepositoryRoot(), "docs", "scenarios.md"));
        var documentedActions = ExtractDocumentedScenarioActions(markdown);
        var supportedActions = ScenarioExecutor.SupportedScenarioActions;
        var missingActions = supportedActions
            .Except(documentedActions, StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var unexpectedActions = documentedActions
            .Except(supportedActions, StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Empty(missingActions);
        Assert.Empty(unexpectedActions);
    }

    [Fact]
    public void Tutorial_Output_Assets_Parse_As_Their_Documented_Formats()
    {
        var outputRoot = Path.GetFullPath(Path.Join(FindRepositoryRoot(), "docs", "assets", "tutorials", "buggy-controller-live-demo", "outputs"));
        Assert.True(Directory.Exists(outputRoot), $"Tutorial output directory '{outputRoot}' was not found.");

        foreach (var json in Directory.GetFiles(outputRoot, "*.json", SearchOption.AllDirectories).Select(File.ReadAllText))
        {
            using var _ = JsonDocument.Parse(json);
        }

        foreach (var asset in Directory.GetFiles(outputRoot, "*.jsonl", SearchOption.AllDirectories)
                     .Select(static jsonlFile => new
                     {
                         File = jsonlFile,
                         Lines = File.ReadLines(jsonlFile)
                             .Where(static line => !string.IsNullOrWhiteSpace(line))
                             .ToArray()
                     }))
        {
            Assert.NotEmpty(asset.Lines);
            foreach (var line in asset.Lines)
            {
                using var _ = JsonDocument.Parse(line);
            }
        }

        foreach (var xmlFile in Directory.GetFiles(outputRoot, "*.xml", SearchOption.AllDirectories))
        {
            _ = XDocument.Load(xmlFile);
        }
    }

    [Fact]
    public async Task Tutorial_And_Example_Scenarios_Are_Discoverable_And_Validate_Without_Device()
    {
        var root = FindRepositoryRoot();
        var artifacts = Path.Join(Path.GetTempPath(), $"luotsi-docs-verify-{Guid.NewGuid():N}");
        var console = new FakeConsole();
        var app = new App(new AppDependencies { Console = console });

        var dryRunExitCode = await app.RunAsync([
            "run",
            "--path",
            Path.Join(root, "examples", "scenarios"),
            "--dry-run",
            "--artifacts",
            artifacts
        ]);
        var validateExitCode = await app.RunAsync([
            "scenario-validate",
            "--path",
            Path.Join(root, "examples", "scenarios"),
            "--artifacts",
            artifacts
        ]);

        Assert.Equal(0, dryRunExitCode);
        Assert.Equal(0, validateExitCode);
        Assert.Equal(2, console.OutputLines.Count);
        using var dryRun = JsonDocument.Parse(console.OutputLines[0]);
        using var validation = JsonDocument.Parse(console.OutputLines[1]);
        Assert.True(dryRun.RootElement.GetProperty("ok").GetBoolean());
        Assert.True(validation.RootElement.GetProperty("ok").GetBoolean());
        Assert.True(dryRun.RootElement.GetProperty("data").GetProperty("selected_count").GetInt32() > 0);
        Assert.Equal("validated", validation.RootElement.GetProperty("data").GetProperty("status").GetString());
    }

    private static IReadOnlyList<string> FindMissingLinks(IEnumerable<string> contentFiles, string contentRoot)
    {
        var missingLinks = new List<string>();

        foreach (var contentFile in contentFiles)
        {
            var content = File.ReadAllText(contentFile);
            foreach (var link in ExtractLocalLinks(content))
            {
                if (!ResolveLocalDocumentationLinkTargets(contentFile, link).Any(TargetExists))
                {
                    missingLinks.Add($"{Path.GetRelativePath(contentRoot, contentFile)} -> {link}");
                }
            }
        }

        return missingLinks;
    }

    private static HashSet<string> ExtractDocumentedCommandPaths(string markdown)
    {
        var documentedCommandPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var commandPath in ExtractCommandPathsFromText(markdown, requireKnownLeadToken: true))
        {
            documentedCommandPaths.Add(commandPath);
        }

        return documentedCommandPaths;
    }

    private static HashSet<string> ExtractHelpCommandPaths()
    {
        var helpPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var topicText in GetHelpTopicTexts().Values)
        {
            foreach (var line in topicText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                         .Where(static line => line.StartsWith("luotsi ", StringComparison.Ordinal)))
            {
                if (TryNormalizeCommandPath(line, requireKnownLeadToken: false, out var commandPath))
                {
                    helpPaths.Add(commandPath);
                }
            }
        }

        return helpPaths;
    }

    private static IReadOnlyDictionary<string, string> GetHelpTopicTexts()
    {
        var field = typeof(Help).GetField("TopicTexts", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field);
        var value = field.GetValue(null);
        var topicTexts = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(value);
        return topicTexts;
    }

    private static IEnumerable<string> ExtractCommandPathsFromText(string text, bool requireKnownLeadToken)
    {
        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (TryNormalizeCommandPath(line, requireKnownLeadToken, out var lineCommandPath))
            {
                yield return lineCommandPath;
            }

            foreach (var code in InlineCodeRegex()
                         .Matches(line)
                         .Cast<Match>()
                         .Select(static match => match.Groups["code"].Value.Trim()))
            {
                if (TryNormalizeCommandPath(code, requireKnownLeadToken, out var codeCommandPath))
                {
                    yield return codeCommandPath;
                }
            }
        }
    }

    private static bool TryNormalizeCommandPath(string candidate, bool requireKnownLeadToken, out string commandPath)
    {
        commandPath = string.Empty;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var normalized = candidate.Trim();
        if (normalized.StartsWith("luotsi ", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[7..].TrimStart();
        }

        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return false;
        }

        var commandTokens = new List<string>();
        foreach (var rawToken in tokens)
        {
            var token = rawToken.Trim(',', '.', ';', ':');
            if (!CommandTokenRegex().IsMatch(token))
            {
                break;
            }

            commandTokens.Add(token);
        }

        if (commandTokens.Count == 0)
        {
            return false;
        }

        if (requireKnownLeadToken && !CliOptions.KnownCommandNames.Contains(commandTokens[0]))
        {
            return false;
        }

        commandPath = string.Join(' ', commandTokens);
        return true;
    }

    private static bool IsTopLevelCommandDocumented(IReadOnlySet<string> documentedCommandPaths, string commandName) =>
        documentedCommandPaths.Contains(commandName)
        || documentedCommandPaths.Any(path => path.StartsWith($"{commandName} ", StringComparison.OrdinalIgnoreCase));

    private static bool IsCommandPathDocumented(IReadOnlySet<string> documentedCommandPaths, string commandPath) =>
        documentedCommandPaths.Contains(commandPath)
        || documentedCommandPaths.Any(path => path.StartsWith($"{commandPath} ", StringComparison.OrdinalIgnoreCase));

    private static HashSet<string> ExtractDocumentedScenarioActions(string markdown)
    {
        const string actionsHeading = "## Actions";
        const string nextHeading = "## Examples";
        var start = markdown.IndexOf(actionsHeading, StringComparison.Ordinal);
        var end = markdown.IndexOf(nextHeading, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Could not locate the Actions section in docs/scenarios.md.");

        var actionsSection = markdown[start..end];
        var documentedActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in actionsSection.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     .Where(static line => line.StartsWith("|", StringComparison.Ordinal)))
        {
            var cells = line.Split('|');
            if (cells.Length < 3)
            {
                continue;
            }

            foreach (var action in InlineCodeRegex()
                         .Matches(cells[1])
                         .Cast<Match>()
                         .Select(static match => match.Groups["code"].Value.Trim())
                         .Where(static value => ScenarioActionTokenRegex().IsMatch(value)))
            {
                documentedActions.Add(action);
            }
        }

        return documentedActions;
    }

    private static IEnumerable<string> ExtractLocalLinks(string markdown)
    {
        foreach (var link in MarkdownLinkRegex()
                     .Matches(markdown)
                     .Cast<Match>()
                     .Select(static match => match.Groups["target"].Value.Trim())
                     .Where(static link => !IsExternalOrAnchorLink(link)))
        {
            yield return link;
        }

        foreach (var link in HtmlSourceRegex()
                     .Matches(markdown)
                     .Cast<Match>()
                     .Select(static match => match.Groups["target"].Value.Trim())
                     .Where(static link => !IsExternalOrAnchorLink(link)))
        {
            yield return link;
        }

        var frontmatter = ExtractFrontmatter(markdown);
        if (frontmatter is not null)
        {
            foreach (var link in FrontmatterLinkRegex()
                         .Matches(frontmatter)
                         .Cast<Match>()
                         .Select(static match => match.Groups["target"].Value.Trim())
                         .Where(static link => !IsExternalOrAnchorLink(link)))
            {
                yield return link;
            }
        }
    }

    private static IEnumerable<string> ResolveLocalDocumentationLinkTargets(string markdownFile, string link)
    {
        var withoutFragment = link.Split('#', 2)[0].Split('?', 2)[0];
        if (string.IsNullOrWhiteSpace(withoutFragment))
        {
            yield break;
        }

        var normalized = withoutFragment
            .Replace('/', Path.DirectorySeparatorChar)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var baseDirectory in ResolveDocumentationLinkBaseDirectories(markdownFile))
        {
            var resolved = Path.GetFullPath(normalized, baseDirectory);
            yield return resolved;

            if (Path.HasExtension(resolved))
            {
                continue;
            }

            yield return $"{resolved}.md";
            yield return $"{resolved}.mdx";
            yield return Path.Join(resolved, "index.md");
            yield return Path.Join(resolved, "index.mdx");
        }
    }

    private static IEnumerable<string> ResolveDocumentationLinkBaseDirectories(string markdownFile)
    {
        var directory = Path.GetDirectoryName(markdownFile)!;
        yield return directory;

        if (string.Equals(Path.GetExtension(markdownFile), ".mdx", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Path.GetFileNameWithoutExtension(markdownFile), "index", StringComparison.OrdinalIgnoreCase))
        {
            yield return Path.Join(directory, Path.GetFileNameWithoutExtension(markdownFile));
        }
    }

    private static IEnumerable<string> ResolveWebsiteSidebarSlugTargets(string websiteDocsRoot, string slug)
    {
        var normalizedSlug = slug.Replace('/', Path.DirectorySeparatorChar).Trim(Path.DirectorySeparatorChar);
        if (string.Equals(normalizedSlug, "docs", StringComparison.OrdinalIgnoreCase))
        {
            yield return Path.Join(websiteDocsRoot, "index.md");
            yield return Path.Join(websiteDocsRoot, "index.mdx");
            yield break;
        }

        if (normalizedSlug.StartsWith($"docs{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        {
            normalizedSlug = normalizedSlug.Substring(5);
        }

        var resolved = Path.GetFullPath(Path.Join(websiteDocsRoot, normalizedSlug));
        yield return resolved;

        if (Path.HasExtension(resolved))
        {
            yield break;
        }

        yield return $"{resolved}.md";
        yield return $"{resolved}.mdx";
        yield return Path.Join(resolved, "index.md");
        yield return Path.Join(resolved, "index.mdx");
    }

    private static bool TargetExists(string path) =>
        File.Exists(path) || Directory.Exists(path);

    private static string? ExtractFrontmatter(string content)
    {
        var match = FrontmatterRegex().Match(content);
        return match.Success ? match.Groups["content"].Value : null;
    }

    private static bool IsExternalOrAnchorLink(string link) =>
        string.IsNullOrWhiteSpace(link) ||
        link.StartsWith("#", StringComparison.Ordinal) ||
        link.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        link.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
        link.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Join(directory.FullName, "Luotsi.sln")) &&
                Directory.Exists(Path.Join(directory.FullName, "docs")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root containing Luotsi.sln and docs.");
    }

    [GeneratedRegex(@"\[[^\]]+\]\((?<target>[^)]+)\)", RegexOptions.Compiled)]
    private static partial Regex MarkdownLinkRegex();

    [GeneratedRegex("`(?<code>[^`]+)`", RegexOptions.Compiled)]
    private static partial Regex InlineCodeRegex();

    [GeneratedRegex("\\b(?:src|href)=\"(?<target>[^\"]+)\"", RegexOptions.Compiled)]
    private static partial Regex HtmlSourceRegex();

    [GeneratedRegex("^[a-z][a-z0-9-]*$", RegexOptions.Compiled)]
    private static partial Regex CommandTokenRegex();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9]*$", RegexOptions.Compiled)]
    private static partial Regex ScenarioActionTokenRegex();

    [GeneratedRegex(@"\A---\r?\n(?<content>.*?)\r?\n---\r?\n", RegexOptions.Compiled | RegexOptions.Singleline)]
    private static partial Regex FrontmatterRegex();

    [GeneratedRegex(@"^\s*link:\s*(?<target>\S+)\s*$", RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex FrontmatterLinkRegex();

    [GeneratedRegex(@"slug:\s*'(?<target>[^']+)'", RegexOptions.Compiled)]
    private static partial Regex SidebarSlugRegex();
}
