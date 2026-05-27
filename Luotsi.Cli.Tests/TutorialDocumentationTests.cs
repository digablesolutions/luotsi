using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Luotsi.Cli.Cli;
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
            foreach (var link in ExtractLocalLinks(content)
                         .Where(link => !ResolveLocalDocumentationLinkTargets(contentFile, link).Any(TargetExists)))
            {
                missingLinks.Add($"{Path.GetRelativePath(contentRoot, contentFile)} -> {link}");
            }
        }

        return missingLinks;
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
        foreach (var resolved in ResolveDocumentationLinkBaseDirectories(markdownFile)
                     .Select(baseDirectory => Path.GetFullPath(normalized, baseDirectory)))
        {
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

    [GeneratedRegex("\\b(?:src|href)=\"(?<target>[^\"]+)\"", RegexOptions.Compiled)]
    private static partial Regex HtmlSourceRegex();

    [GeneratedRegex(@"\A---\r?\n(?<content>.*?)\r?\n---\r?\n", RegexOptions.Compiled | RegexOptions.Singleline)]
    private static partial Regex FrontmatterRegex();

    [GeneratedRegex(@"^\s*link:\s*(?<target>\S+)\s*$", RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex FrontmatterLinkRegex();

    [GeneratedRegex(@"slug:\s*'(?<target>[^']+)'", RegexOptions.Compiled)]
    private static partial Regex SidebarSlugRegex();
}
