using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Luotsi.Cli.Tests;

public sealed partial class AppTests
{
    [Fact]
    public void Tutorial_Documentation_Links_Resolve()
    {
        var docsRoot = Path.Combine(FindRepositoryRoot(), "docs");
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
        var outputRoot = Path.Combine(FindRepositoryRoot(), "docs", "assets", "tutorials", "buggy-controller-live-demo", "outputs");
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
            foreach (var document in asset.Lines.Select(static line => JsonDocument.Parse(line)))
            {
                using var _ = document;
            }
        }

        foreach (var xmlFile in Directory.GetFiles(outputRoot, "*.xml", SearchOption.AllDirectories))
        {
            _ = XDocument.Load(xmlFile);
        }
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
            if (File.Exists(Path.Combine(directory.FullName, "Luotsi.sln")) && Directory.Exists(Path.Combine(directory.FullName, "docs")))
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
