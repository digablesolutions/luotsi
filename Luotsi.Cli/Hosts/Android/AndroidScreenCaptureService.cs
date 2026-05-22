using System.Xml;
using System.Xml.Linq;
using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Hosts.Android;

internal sealed record ScreenCapture(string Xml, ScreenState State);

internal sealed record HierarchyDumpAttempt(
    string Strategy,
    string Command,
    int ExitCode,
    bool Succeeded,
    bool XmlExtracted,
    string Stdout,
    string Stderr);

internal sealed class AndroidScreenCaptureService(
    IAdbClient adb,
    ArtifactSession artifacts,
    TimeProvider timeProvider,
    IDelay delay,
    IFileSystem fileSystem)
{
    private readonly IAdbClient _adb = adb ?? throw new ArgumentNullException(nameof(adb));
    private readonly ArtifactSession _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly IDelay _delay = delay ?? throw new ArgumentNullException(nameof(delay));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    private UiDumpSnapshot? _uiDumpCache;

    public async Task<ScreenState> GetScreenStateAsync()
    {
        var capture = await ReadScreenCaptureAsync(writeInvalidArtifact: true).ConfigureAwait(false);
        await WriteScreenCaptureArtifactsAsync(capture).ConfigureAwait(false);
        return capture.State;
    }

    public async Task<ScreenState> CaptureScreenStateAsync(string? snapshotPrefix)
    {
        var capture = await ReadScreenCaptureAsync(writeInvalidArtifact: true).ConfigureAwait(false);
        await WriteScreenCaptureArtifactsAsync(capture, snapshotPrefix).ConfigureAwait(false);
        return capture.State;
    }

    public async Task<ScreenCapture> CapturePollingScreenStateAsync(string snapshotPrefix)
    {
        var writePerAttemptArtifacts = _artifacts.UiPollArtifactPolicy == UiPollArtifactPolicy.PerAttempt;
        var capture = await ReadScreenCaptureAsync(writeInvalidArtifact: writePerAttemptArtifacts).ConfigureAwait(false);
        if (writePerAttemptArtifacts)
        {
            await WriteScreenCaptureArtifactsAsync(capture, snapshotPrefix).ConfigureAwait(false);
        }

        return capture;
    }

    public Task PersistPollingArtifactsAsync(ScreenCapture capture, string snapshotPrefix) =>
        _artifacts.UiPollArtifactPolicy switch
        {
            UiPollArtifactPolicy.Final => WriteScreenCaptureArtifactsAsync(capture, snapshotPrefix),
            UiPollArtifactPolicy.PerAttempt or UiPollArtifactPolicy.None => Task.CompletedTask,
            _ => throw new InvalidOperationException($"Unsupported UI poll artifact policy '{_artifacts.UiPollArtifactPolicy}'.")
        };

    public async Task<XDocument> LoadUiDocumentWithRetryAsync(
        int maxAttempts = AndroidRuntimeDefaults.UiDumpRetryMaxAttempts,
        int retryDelayMs = AndroidRuntimeDefaults.UiPollDelayMs)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await LoadUiDocumentAsync().ConfigureAwait(false);
            }
            catch (InvalidOperationException ex) when (attempt < maxAttempts && IsRetryableHierarchyDumpFailure(ex))
            {
                await _delay.DelayAsync(retryDelayMs).ConfigureAwait(false);
            }
        }
    }

    public void InvalidateUiDumpCache() => _uiDumpCache = null;

    public static bool IsRetryableHierarchyDumpFailure(InvalidOperationException exception) =>
        exception is ScreenStateUnavailableException;

    private async Task<ScreenCapture> ReadScreenCaptureAsync(bool writeInvalidArtifact)
    {
        var xml = await ReadUiDumpXmlAsync().ConfigureAwait(false);

        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
            CacheUiDump(xml);
        }
        catch (Exception ex) when (ex is XmlException or InvalidOperationException)
        {
            InvalidateUiDumpCache();
            if (writeInvalidArtifact)
            {
                await _artifacts.WriteTextAsync(DeviceArtifactNames.HierarchyXml, xml).ConfigureAwait(false);
                await _artifacts.WriteTextAsync(DeviceArtifactNames.InvalidHierarchyXml, xml).ConfigureAwait(false);
            }

            throw new ScreenStateUnavailableException(
                $"UI hierarchy dump did not contain parseable XML. See {DeviceArtifactNames.InvalidHierarchyXml} for the raw dump and {DeviceArtifactNames.HierarchyDumpAttemptsJson} for attempt details.",
                ex);
        }

        var elements = document.Descendants("node")
            .Select(static node => ScreenElement.From(node))
            .Where(static element => element.IsUseful)
            .ToArray();
        return new ScreenCapture(xml, new ScreenState(_timeProvider.GetUtcNow(), elements.Length, elements));
    }

    private async Task WriteScreenCaptureArtifactsAsync(ScreenCapture capture, string? snapshotPrefix = null)
    {
        await _artifacts.WriteTextAsync(DeviceArtifactNames.HierarchyXml, capture.Xml).ConfigureAwait(false);
        await _artifacts.WriteJsonAsync(DeviceArtifactNames.ScreenStateJson, capture.State).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(snapshotPrefix))
        {
            return;
        }

        var screenStatePath = Path.Combine(_artifacts.Root, DeviceArtifactNames.ScreenStateJson);
        var hierarchyPath = Path.Combine(_artifacts.Root, DeviceArtifactNames.HierarchyXml);
        _fileSystem.CopyFile(screenStatePath, Path.Combine(_artifacts.Root, DeviceArtifactNames.ScreenStateForLabel(snapshotPrefix)), true);
        _fileSystem.CopyFile(hierarchyPath, Path.Combine(_artifacts.Root, DeviceArtifactNames.HierarchyForLabel(snapshotPrefix)), true);

        var invalidHierarchyPath = Path.Combine(_artifacts.Root, DeviceArtifactNames.InvalidHierarchyXml);
        if (_fileSystem.FileExists(invalidHierarchyPath))
        {
            _fileSystem.CopyFile(invalidHierarchyPath, Path.Combine(_artifacts.Root, DeviceArtifactNames.InvalidHierarchyForLabel(snapshotPrefix)), true);
        }
    }

    private async Task<XDocument> LoadUiDocumentAsync()
    {
        var xml = await ReadUiDumpXmlAsync().ConfigureAwait(false);
        try
        {
            var document = XDocument.Parse(xml);
            CacheUiDump(xml);
            return document;
        }
        catch (Exception ex) when (ex is XmlException or InvalidOperationException)
        {
            InvalidateUiDumpCache();
            await _artifacts.WriteTextAsync(DeviceArtifactNames.InvalidHierarchyXml, xml).ConfigureAwait(false);
            throw new ScreenStateUnavailableException(
                $"UI hierarchy dump did not contain parseable XML. See {DeviceArtifactNames.InvalidHierarchyXml} and {DeviceArtifactNames.HierarchyDumpAttemptsJson} for raw dump output.",
                ex);
        }
    }

    private async Task<string> ReadUiDumpXmlAsync()
    {
        var now = _timeProvider.GetUtcNow();
        if (_uiDumpCache is { } cached && now - cached.CapturedAt < AndroidRuntimeDefaults.UiDumpCacheTtl)
        {
            return cached.Xml;
        }

        return await DumpUiAsync().ConfigureAwait(false);
    }

    private async Task<string> DumpUiAsync()
    {
        var attempts = new List<HierarchyDumpAttempt>();
        var primary = await RunFileBackedDumpAsync("file:/data/local/tmp", "/data/local/tmp/luotsi-window.xml").ConfigureAwait(false);
        attempts.Add(primary.Attempt);
        if (primary.Xml is not null)
        {
            await WriteDumpAttemptsAsync(attempts).ConfigureAwait(false);
            return primary.Xml;
        }

        var secondary = await RunFileBackedDumpAsync("file:/sdcard", "/sdcard/window_dump.xml").ConfigureAwait(false);
        attempts.Add(secondary.Attempt);
        if (secondary.Xml is not null)
        {
            await WriteDumpAttemptsAsync(attempts).ConfigureAwait(false);
            return secondary.Xml;
        }

        var fallback = await RunStdoutDumpAsync().ConfigureAwait(false);
        attempts.Add(fallback.Attempt);
        await WriteDumpAttemptsAsync(attempts).ConfigureAwait(false);
        if (fallback.Xml is not null)
        {
            return fallback.Xml;
        }

        return attempts.LastOrDefault(static attempt => !string.IsNullOrWhiteSpace(attempt.Stdout))?.Stdout
            ?? string.Join(Environment.NewLine, attempts.Select(static attempt => attempt.Stderr).Where(static value => !string.IsNullOrWhiteSpace(value)));
    }

    private async Task<(HierarchyDumpAttempt Attempt, string? Xml)> RunFileBackedDumpAsync(string strategy, string remotePath)
    {
        var command = $"rm -f {ShellQuote(remotePath)}; uiautomator dump {ShellQuote(remotePath)} >/dev/null 2>&1; cat {ShellQuote(remotePath)}; rm -f {ShellQuote(remotePath)}";
        var result = await _adb.ShellAsync(command).ConfigureAwait(false);
        var xml = TryExtractHierarchyXml(result.Stdout);
        return (CreateAttempt(strategy, command, result.ExitCode, result.Stdout, result.Stderr, xml is not null), xml);
    }

    private async Task<(HierarchyDumpAttempt Attempt, string? Xml)> RunStdoutDumpAsync()
    {
        var result = await _adb.RunAsync(["exec-out", "uiautomator", "dump", "/dev/tty"]).ConfigureAwait(false);
        var xml = TryExtractHierarchyXml(result.Stdout);
        return (CreateAttempt("stdout:/dev/tty", "exec-out uiautomator dump /dev/tty", result.ExitCode, result.Stdout, result.Stderr, xml is not null), xml);
    }

    private static HierarchyDumpAttempt CreateAttempt(string strategy, string command, int exitCode, string stdout, string stderr, bool xmlExtracted) =>
        new(strategy, command, exitCode, exitCode == 0, xmlExtracted, stdout, stderr);

    private Task WriteDumpAttemptsAsync(IReadOnlyList<HierarchyDumpAttempt> attempts) =>
        _artifacts.WriteJsonAsync(DeviceArtifactNames.HierarchyDumpAttemptsJson, attempts);

    private static string? TryExtractHierarchyXml(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var xmlStart = value.IndexOf("<?xml", StringComparison.Ordinal);
        if (xmlStart < 0)
        {
            xmlStart = value.IndexOf("<hierarchy", StringComparison.Ordinal);
        }

        var xmlEnd = value.LastIndexOf("</hierarchy>", StringComparison.Ordinal);
        if (xmlStart >= 0 && xmlEnd >= xmlStart)
        {
            xmlEnd += "</hierarchy>".Length;
            return value[xmlStart..xmlEnd];
        }

        return null;
    }

    private void CacheUiDump(string xml) => _uiDumpCache = new UiDumpSnapshot(xml, _timeProvider.GetUtcNow());

    private static string ShellQuote(string value) => $"'{value.Replace("'", "'\\''", StringComparison.Ordinal)}'";

    private sealed record UiDumpSnapshot(string Xml, DateTimeOffset CapturedAt);
}
