using System.Xml;
using System.Xml.Linq;
using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Hosts.Android;

internal sealed record ScreenCapture(string Xml, ScreenState State);

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
        exception.Message.Contains("UI hierarchy dump was empty or invalid XML", StringComparison.OrdinalIgnoreCase);

    private async Task<ScreenCapture> ReadScreenCaptureAsync(bool writeInvalidArtifact)
    {
        var xml = await ReadUiDumpXmlAsync().ConfigureAwait(false);

        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
            CacheUiDump(xml);
        }
        catch (Exception ex) when (ex is XmlException || ex is InvalidOperationException)
        {
            InvalidateUiDumpCache();
            if (writeInvalidArtifact)
            {
                await _artifacts.WriteTextAsync(DeviceArtifactNames.HierarchyXml, xml).ConfigureAwait(false);
                await _artifacts.WriteTextAsync(DeviceArtifactNames.InvalidHierarchyXml, xml).ConfigureAwait(false);
            }

            throw new InvalidOperationException($"UI hierarchy dump was empty or invalid XML. See {DeviceArtifactNames.InvalidHierarchyXml} for the raw dump.", ex);
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
        catch (Exception ex) when (ex is XmlException || ex is InvalidOperationException)
        {
            InvalidateUiDumpCache();
            await _artifacts.WriteTextAsync(DeviceArtifactNames.InvalidHierarchyXml, xml).ConfigureAwait(false);
            throw new InvalidOperationException("UI hierarchy dump was empty or invalid XML.", ex);
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
        var result = await _adb.RunAsync(["exec-out", "uiautomator", "dump", "/dev/tty"]).ConfigureAwait(false);
        result.EnsureSuccess("uiautomator dump failed");
        var xml = result.Stdout;
        var xmlStart = xml.IndexOf("<?xml", StringComparison.Ordinal);
        if (xmlStart < 0)
        {
            xmlStart = xml.IndexOf("<hierarchy", StringComparison.Ordinal);
        }

        var xmlEnd = xml.LastIndexOf("</hierarchy>", StringComparison.Ordinal);
        if (xmlStart >= 0 && xmlEnd >= xmlStart)
        {
            xmlEnd += "</hierarchy>".Length;
            return xml[xmlStart..xmlEnd];
        }

        return xmlStart >= 0 ? xml[xmlStart..] : xml;
    }

    private void CacheUiDump(string xml) => _uiDumpCache = new UiDumpSnapshot(xml, _timeProvider.GetUtcNow());

    private sealed record UiDumpSnapshot(string Xml, DateTimeOffset CapturedAt);
}