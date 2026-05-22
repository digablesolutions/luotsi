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
        var missingLinks = new List<string>();

        foreach (var markdownFile in markdownFiles)
        {
            var markdown = File.ReadAllText(markdownFile);
            foreach (var link in ExtractLocalLinks(markdown))
            {
                var target = ResolveLocalDocumentationLink(markdownFile, link);
                if (target is not null && !File.Exists(target) && !Directory.Exists(target))
                {
                    missingLinks.Add($"{Path.GetRelativePath(docsRoot, markdownFile)} -> {link}");
                }
            }
        }

        Assert.Empty(missingLinks);
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
    }

    private static string? ResolveLocalDocumentationLink(string markdownFile, string link)
    {
        var withoutFragment = link.Split('#', 2)[0];
        if (string.IsNullOrWhiteSpace(withoutFragment))
        {
            return null;
        }

        return Path.GetFullPath(withoutFragment.Replace('/', Path.DirectorySeparatorChar), Path.GetDirectoryName(markdownFile)!);
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
}
