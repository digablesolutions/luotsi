using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Hosts.Android;

internal sealed class AndroidScreenStateReadModel(
    IAdbClient adb,
    AndroidScreenCaptureService screenCapture,
    TimeProvider timeProvider,
    IDelay delay)
{
    private readonly IAdbClient _adb = adb ?? throw new ArgumentNullException(nameof(adb));
    private readonly AndroidScreenCaptureService _screenCapture = screenCapture ?? throw new ArgumentNullException(nameof(screenCapture));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly IDelay _delay = delay ?? throw new ArgumentNullException(nameof(delay));

    private (int Width, int Height)? _displaySizeCache;
    private KeyboardVisibilitySnapshot? _keyboardVisibilityCache;

    public Task<ScreenState> GetScreenStateAsync() =>
        _screenCapture.GetScreenStateAsync();

    public Task<ScreenState> CaptureScreenStateAsync(string? snapshotPrefix) =>
        _screenCapture.CaptureScreenStateAsync(snapshotPrefix);

    public Task<ScreenCapture> CapturePollingScreenStateAsync(string snapshotPrefix) =>
        _screenCapture.CapturePollingScreenStateAsync(snapshotPrefix);

    public Task PersistPollingArtifactsAsync(ScreenCapture capture, string snapshotPrefix) =>
        _screenCapture.PersistPollingArtifactsAsync(capture, snapshotPrefix);

    public Task<XDocument> LoadUiDocumentWithRetryAsync(
        int maxAttempts = AndroidRuntimeDefaults.UiDumpRetryMaxAttempts,
        int retryDelayMs = AndroidRuntimeDefaults.UiPollDelayMs) =>
        _screenCapture.LoadUiDocumentWithRetryAsync(maxAttempts, retryDelayMs);

    public async Task<ScreenState> CaptureScreenStateWithRetryAsync(string? snapshotPrefix, int maxAttempts = 3, int retryDelayMs = 250)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await CaptureScreenStateAsync(snapshotPrefix).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex) when (attempt < maxAttempts && AndroidScreenCaptureService.IsRetryableHierarchyDumpFailure(ex))
            {
                await _delay.DelayAsync(retryDelayMs).ConfigureAwait(false);
            }
        }
    }

    public async Task<bool> IsKeyboardVisibleAsync()
    {
        var now = _timeProvider.GetUtcNow();
        if (_keyboardVisibilityCache is { } cached && now - cached.CapturedAt < AndroidRuntimeDefaults.KeyboardVisibilityCacheTtl)
        {
            return cached.IsVisible;
        }

        var result = await ShellTextAsync("dumpsys input_method | grep -E 'mInputShown=true|mIsInputViewShown=true|mShowRequested=true' | head -1").ConfigureAwait(false);
        var isVisible = !string.IsNullOrWhiteSpace(result);
        _keyboardVisibilityCache = new KeyboardVisibilitySnapshot(isVisible, now);
        return isVisible;
    }

    public async Task<(int X, int Y)> ResolveRelativePointAsync(double? xRatio, double? yRatio)
    {
        if (!xRatio.HasValue || !yRatio.HasValue)
        {
            throw new UsageException("tapPoint requires either x/y or xRatio/yRatio.");
        }

        var validatedXRatio = RequireUnitInterval(xRatio.Value, "tapPoint xRatio must be between 0 and 1.");
        var validatedYRatio = RequireUnitInterval(yRatio.Value, "tapPoint yRatio must be between 0 and 1.");

        var (width, height) = await GetDisplaySizeAsync().ConfigureAwait(false);
        var resolvedX = (int)Math.Round(width * validatedXRatio, MidpointRounding.AwayFromZero);
        var resolvedY = (int)Math.Round(height * validatedYRatio, MidpointRounding.AwayFromZero);
        return (resolvedX, resolvedY);
    }

    public async Task<(int X, int Y)> ResolveHeaderLogoTargetAsync()
    {
        var document = await LoadUiDocumentWithRetryAsync().ConfigureAwait(false);
        var (width, _) = await GetDisplaySizeAsync().ConfigureAwait(false);
        var centerX = width / 2d;

        var candidates = document.Descendants("node")
            .Where(node => string.Equals((string?)node.Attribute("class"), "android.widget.ImageView", StringComparison.Ordinal))
            .Select(node => ParseNodeBounds((string?)node.Attribute("bounds") ?? "[0,0][0,0]"))
            .Where(bounds => bounds.Top <= 140)
            .Select(bounds => new
            {
                Bounds = bounds,
                Delta = Math.Abs((bounds.Left + bounds.Right) / 2d - centerX)
            })
            .Where(candidate => candidate.Delta <= width * 0.2)
            .OrderBy(candidate => candidate.Delta)
            .ThenBy(candidate => candidate.Bounds.Top)
            .ToArray();

        var match = candidates.FirstOrDefault() ?? throw new InvalidOperationException("Could not find a header logo target near the top center of the screen.");
        return ((match.Bounds.Left + match.Bounds.Right) / 2, (match.Bounds.Top + match.Bounds.Bottom) / 2);
    }

    public async Task<(int Width, int Height)> GetDisplaySizeAsync()
    {
        if (_displaySizeCache.HasValue)
        {
            return _displaySizeCache.Value;
        }

        var text = await ShellTextAsync("wm size").ConfigureAwait(false);
        var match = Regex.Match(text, @"(?<width>\d+)x(?<height>\d+)");
        if (!match.Success)
        {
            throw new InvalidOperationException($"Could not parse device display size from '{text}'.");
        }

        _displaySizeCache = (
            int.Parse(match.Groups["width"].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups["height"].Value, CultureInfo.InvariantCulture));
        return _displaySizeCache.Value;
    }

    public void InvalidateUiReadCaches()
    {
        _keyboardVisibilityCache = null;
        _screenCapture.InvalidateUiDumpCache();
    }

    private async Task<string> ShellTextAsync(string command)
    {
        var result = await _adb.ShellAsync(command).ConfigureAwait(false);
        result.EnsureSuccess($"adb shell failed: {command}");
        return result.Stdout.Trim();
    }

    private static Bounds ParseNodeBounds(string value)
    {
        var numbers = value.Split(['[', ']', ','], StringSplitOptions.RemoveEmptyEntries)
            .Select(static part => int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0)
            .ToArray();
        return numbers.Length >= 4 ? new Bounds(numbers[0], numbers[1], numbers[2], numbers[3]) : new Bounds(0, 0, 0, 0);
    }

    private static double RequireUnitInterval(double value, string message)
    {
        if (value is < 0 or > 1)
        {
            throw new UsageException(message);
        }

        return value;
    }

    private sealed record KeyboardVisibilitySnapshot(bool IsVisible, DateTimeOffset CapturedAt);
}