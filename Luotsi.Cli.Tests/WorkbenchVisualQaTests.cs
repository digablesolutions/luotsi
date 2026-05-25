using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Infrastructure.System;
using Microsoft.Playwright;
using Xunit;
using Xunit.Sdk;

namespace Luotsi.Cli.Tests;

public sealed class WorkbenchVisualQaTests
{
    [Fact]
    public async Task Replay_Workbench_Fixture_Renders_Core_Panels_In_Browser()
    {
        var root = CopyReplayWorkbenchFixtureToTemp();
        try
        {
            var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(root, file))
                .OrderBy(ArtifactIndexRenderer.GetArtifactSortGroup)
                .ThenBy(static file => file, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var renderer = new ArtifactIndexRenderer(root, new PhysicalFileSystem());
            await File.WriteAllTextAsync(Path.Join(root, "index.html"), await renderer.BuildHtmlIndexAsync(files));

            using var playwright = await Playwright.CreateAsync();
            var browser = await LaunchHostBrowserAsync(playwright);
            await using (browser)
            {
                var page = await browser.NewPageAsync(new BrowserNewPageOptions
                {
                    ViewportSize = new ViewportSize { Width = 1440, Height = 1000 }
                });

                await page.GotoAsync(new Uri(Path.Join(root, "index.html")).AbsoluteUri);
                await AssertHasVisibleBoxAsync(page.Locator("header"));
                await AssertHasVisibleBoxAsync(page.Locator("#failure-workbench"));
                await AssertHasVisibleBoxAsync(page.Locator(".workbench-layout"));
                await AssertHasVisibleBoxAsync(page.Locator(".hero-panel"));
                await AssertHasVisibleBoxAsync(page.Locator(".workbench-side"));
                await AssertHasVisibleBoxAsync(page.Locator(".media-grid"));
                await AssertHasVisibleBoxAsync(page.Locator(".timeline"));
                await AssertHasVisibleBoxAsync(page.Locator("#replay-front-door"));
                await AssertFitsViewportAsync(page);

                await page.SetViewportSizeAsync(390, 844);
                await AssertHasVisibleBoxAsync(page.Locator("#failure-workbench"));
                await AssertHasVisibleBoxAsync(page.Locator(".hero-panel"));
                await AssertFitsViewportAsync(page);

                var screenshot = await page.ScreenshotAsync(new PageScreenshotOptions { FullPage = false });
                Assert.True(screenshot.Length > 4096, "Rendered workbench screenshot should contain visible page content.");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<IBrowser> LaunchHostBrowserAsync(IPlaywright playwright)
    {
        var channels = OperatingSystem.IsWindows()
            ? new[] { "msedge", "chrome" }
            : new[] { "chrome", "msedge" };
        var failures = new List<string>();
        foreach (var channel in channels)
        {
            try
            {
                return await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Channel = channel,
                    Headless = true
                });
            }
            catch (PlaywrightException ex)
            {
                failures.Add($"{channel}: {ex.Message}");
            }
        }

        var message = "Unable to launch an installed Chromium-family browser for workbench visual QA. " + string.Join(" | ", failures);
        if (string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(message);
        }

        throw SkipException.ForSkip(message);
    }

    private static async Task AssertHasVisibleBoxAsync(ILocator locator)
    {
        await locator.First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5000
        });
        var box = await locator.First.BoundingBoxAsync();
        Assert.NotNull(box);
        Assert.True(box.Width > 0, $"Expected {locator} to have a non-zero rendered width.");
        Assert.True(box.Height > 0, $"Expected {locator} to have a non-zero rendered height.");
    }

    private static async Task AssertFitsViewportAsync(IPage page)
    {
        var fits = await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth + 1");
        if (!fits)
        {
            var metrics = await page.EvaluateAsync<string>(
                "() => `scrollWidth=${document.documentElement.scrollWidth}; innerWidth=${window.innerWidth}`");
            throw new InvalidOperationException("Workbench layout overflowed the viewport: " + metrics);
        }
    }

    private static string CopyReplayWorkbenchFixtureToTemp()
    {
        var source = Path.Join(AppContext.BaseDirectory, "Fixtures", "ReplayWorkbench", "failure");
        var target = Path.Join(Path.GetTempPath(), "luotsi-workbench-visual-qa", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var destination = Path.Join(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }

        return target;
    }
}
