using System.Text.RegularExpressions;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Hosts.Android;

internal sealed class AndroidUiInteractionService(
    IAdbClient adb,
    AndroidScreenStateReadModel screenStateReadModel,
    TimeProvider timeProvider,
    IDelay delay,
    IEnvironmentVariables environment)
{
    private readonly IAdbClient _adb = adb ?? throw new ArgumentNullException(nameof(adb));
    private readonly AndroidScreenStateReadModel _screenStateReadModel = screenStateReadModel ?? throw new ArgumentNullException(nameof(screenStateReadModel));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly IDelay _delay = delay ?? throw new ArgumentNullException(nameof(delay));
    private readonly IEnvironmentVariables _environment = environment ?? throw new ArgumentNullException(nameof(environment));

    public async Task<ScreenElement> WaitVisibleAsync(string text, int timeoutSec)
    {
        var expectedText = RequireNonBlank(text, "waitVisible requires non-empty text.");
        var validatedTimeoutSec = RequirePositive(timeoutSec, "waitVisible requires timeoutSec greater than zero.");
        var deadline = _timeProvider.GetUtcNow().AddSeconds(validatedTimeoutSec);
        ScreenElement? last = null;
        var attempt = 0;
        while (_timeProvider.GetUtcNow() < deadline)
        {
            attempt++;
            var snapshotPrefix = $"wait-visible-{attempt:000}";
            ScreenCapture capture;

            try
            {
                capture = await _screenStateReadModel.CapturePollingScreenStateAsync(snapshotPrefix).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex) when (AndroidScreenCaptureService.IsRetryableHierarchyDumpFailure(ex))
            {
                await _delay.DelayAsync(AndroidRuntimeDefaults.UiPollDelayMs).ConfigureAwait(false);
                continue;
            }

            last = capture.State.Elements
                .Select(element => new { Element = element, Score = element.GetMatchScore(expectedText) })
                .Where(candidate => candidate.Score > 0)
                .OrderByDescending(candidate => candidate.Score)
                .Select(candidate => candidate.Element)
                .FirstOrDefault();
            if (last is not null)
            {
                await _screenStateReadModel.PersistPollingArtifactsAsync(capture, snapshotPrefix).ConfigureAwait(false);
                return last;
            }

            await _delay.DelayAsync(AndroidRuntimeDefaults.UiPollDelayMs).ConfigureAwait(false);
        }

        throw new TimeoutException($"Timed out after {validatedTimeoutSec}s waiting for visible text '{expectedText}'. Last seen: {last?.StableId ?? "none"}");
    }

    public async Task<TapResult> TapTextAsync(string text, int timeoutSec)
    {
        var element = await WaitVisibleAsync(text, timeoutSec).ConfigureAwait(false);
        return await TapAsync(element.CenterX.ToString(), element.CenterY.ToString()).ConfigureAwait(false);
    }

    public async Task<TapResult> TapAsync(string x, string y)
    {
        if (!int.TryParse(x, out var parsedX))
        {
            throw new UsageException("Option --x must be an integer.");
        }

        if (!int.TryParse(y, out var parsedY))
        {
            throw new UsageException("Option --y must be an integer.");
        }

        if (parsedX < 0 || parsedY < 0)
        {
            throw new UsageException("Tap coordinates must be zero or greater.");
        }

        var result = await _adb.ShellAsync($"input tap {parsedX} {parsedY}").ConfigureAwait(false);
        result.EnsureSuccess("tap failed");
        _screenStateReadModel.InvalidateUiReadCaches();
        return new TapResult(parsedX, parsedY);
    }

    public async Task<TypeTextResult> TypeTextAsync(string text)
    {
        var escaped = text.Replace("%", "%25", StringComparison.Ordinal).Replace(" ", "%s", StringComparison.Ordinal);
        var result = await _adb.ShellAsync($"input text {ShellQuote(escaped)}").ConfigureAwait(false);
        result.EnsureSuccess("type text failed");
        _screenStateReadModel.InvalidateUiReadCaches();
        return new TypeTextResult(text);
    }

    public async Task<KeyEventResult> KeyEventAsync(string code)
    {
        var keyCode = RequireNonBlank(code, "keyevent requires code.");
        var result = await _adb.ShellAsync($"input keyevent {ShellQuote(keyCode)}").ConfigureAwait(false);
        result.EnsureSuccess("keyevent failed");
        _screenStateReadModel.InvalidateUiReadCaches();
        return new KeyEventResult(keyCode);
    }

    public async Task<ScrollResult> ScrollAsync(int horizontalTicks, int verticalTicks)
    {
        if (horizontalTicks == 0 && verticalTicks == 0)
        {
            throw new UsageException("scroll requires at least one non-zero wheel delta.");
        }

        var (displayWidth, displayHeight) = await _screenStateReadModel.GetDisplaySizeAsync().ConfigureAwait(false);
        var width = Math.Max(320, displayWidth);
        var height = Math.Max(480, displayHeight);
        var centerX = width / 2;
        var centerY = height / 2;
        var horizontalDistance = Math.Clamp(Math.Abs(horizontalTicks) * Math.Max(80, width / 4), 80, Math.Max(120, width / 2));
        var verticalDistance = Math.Clamp(Math.Abs(verticalTicks) * Math.Max(80, height / 4), 80, Math.Max(120, height / 2));
        const int durationMs = 180;

        var startX = centerX;
        var endX = centerX;
        var startY = centerY;
        var endY = centerY;

        if (Math.Abs(verticalTicks) >= Math.Abs(horizontalTicks) && verticalTicks != 0)
        {
            var halfDistance = verticalDistance / 2;
            if (verticalTicks > 0)
            {
                startY = Math.Max(0, centerY - halfDistance);
                endY = Math.Min(height - 1, centerY + halfDistance);
            }
            else
            {
                startY = Math.Min(height - 1, centerY + halfDistance);
                endY = Math.Max(0, centerY - halfDistance);
            }
        }
        else
        {
            var halfDistance = horizontalDistance / 2;
            if (horizontalTicks > 0)
            {
                startX = Math.Min(width - 1, centerX + halfDistance);
                endX = Math.Max(0, centerX - halfDistance);
            }
            else
            {
                startX = Math.Max(0, centerX - halfDistance);
                endX = Math.Min(width - 1, centerX + halfDistance);
            }
        }

        var result = await _adb.ShellAsync($"input swipe {startX} {startY} {endX} {endY} {durationMs}").ConfigureAwait(false);
        result.EnsureSuccess("scroll failed");
        _screenStateReadModel.InvalidateUiReadCaches();
        return new ScrollResult(horizontalTicks, verticalTicks, startX, startY, endX, endY, durationMs);
    }

    public async Task<WaitNotVisibleResult> WaitNotVisibleAsync(string text, int timeoutSec)
    {
        var expectedText = RequireNonBlank(text, "waitNotVisible requires text.");
        var validatedTimeoutSec = RequirePositive(timeoutSec, "waitNotVisible requires timeoutSec greater than zero.");
        var deadline = _timeProvider.GetUtcNow().AddSeconds(validatedTimeoutSec);
        var attempt = 0;

        while (_timeProvider.GetUtcNow() < deadline)
        {
            attempt++;
            var snapshotPrefix = $"wait-not-visible-{attempt:000}";
            ScreenCapture capture;

            try
            {
                capture = await _screenStateReadModel.CapturePollingScreenStateAsync(snapshotPrefix).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex) when (AndroidScreenCaptureService.IsRetryableHierarchyDumpFailure(ex))
            {
                await _delay.DelayAsync(AndroidRuntimeDefaults.UiPollDelayMs).ConfigureAwait(false);
                continue;
            }

            if (!capture.State.Elements.Any(element => element.Matches(expectedText)))
            {
                await _screenStateReadModel.PersistPollingArtifactsAsync(capture, snapshotPrefix).ConfigureAwait(false);
                return new WaitNotVisibleResult(expectedText, attempt, false);
            }

            await _delay.DelayAsync(AndroidRuntimeDefaults.UiPollDelayMs).ConfigureAwait(false);
        }

        throw new TimeoutException($"Timed out after {validatedTimeoutSec}s waiting for text '{expectedText}' to disappear.");
    }

    public async Task<TapPointResult> TapPointAsync(string? label, int? x, int? y, double? xRatio, double? yRatio, int postTapDelayMs)
    {
        var validatedPostTapDelayMs = RequireNonNegative(postTapDelayMs, "tapPoint postTapDelayMs must be zero or greater.");

        if (x.HasValue != y.HasValue)
        {
            throw new UsageException("tapPoint requires both x and y when using absolute coordinates.");
        }

        if (x is < 0 || y is < 0)
        {
            throw new UsageException("tapPoint coordinates must be zero or greater.");
        }

        var (resolvedX, resolvedY) = x.HasValue && y.HasValue
            ? (x.Value, y.Value)
            : await _screenStateReadModel.ResolveRelativePointAsync(xRatio, yRatio).ConfigureAwait(false);

        var result = await _adb.ShellAsync($"input tap {resolvedX} {resolvedY}").ConfigureAwait(false);
        result.EnsureSuccess("tap point failed");
        _screenStateReadModel.InvalidateUiReadCaches();

        if (validatedPostTapDelayMs > 0)
        {
            await _delay.DelayAsync(validatedPostTapDelayMs).ConfigureAwait(false);
        }

        return new TapPointResult(label, resolvedX, resolvedY, xRatio, yRatio, validatedPostTapDelayMs);
    }

    public async Task<DoubleTapHeaderLogoResult> DoubleTapHeaderLogoAsync()
    {
        var (x, y) = await _screenStateReadModel.ResolveHeaderLogoTargetAsync().ConfigureAwait(false);
        foreach (var _ in Enumerable.Range(0, 2))
        {
            var tap = await _adb.ShellAsync($"input tap {x} {y}").ConfigureAwait(false);
            tap.EnsureSuccess("double tap header logo failed");
            await _delay.DelayAsync(160).ConfigureAwait(false);
        }

        _screenStateReadModel.InvalidateUiReadCaches();

        return new DoubleTapHeaderLogoResult("header_logo", x, y, 160);
    }

    public async Task<TypePinResult> TypePinAsync(string pin, int perDigitDelayMs)
    {
        if (string.IsNullOrWhiteSpace(pin))
        {
            throw new UsageException("typePin requires text.");
        }

        var validatedPerDigitDelayMs = RequireNonNegative(perDigitDelayMs, "typePin intervalMs must be zero or greater.");

        var digits = pin.Trim();
        foreach (var digit in digits)
        {
            if (!char.IsDigit(digit))
            {
                throw new UsageException($"typePin supports digits only. Invalid character '{digit}'.");
            }

            var result = await _adb.ShellAsync($"input keyevent KEYCODE_{digit}").ConfigureAwait(false);
            result.EnsureSuccess("type pin failed");

            if (validatedPerDigitDelayMs > 0)
            {
                await _delay.DelayAsync(validatedPerDigitDelayMs).ConfigureAwait(false);
            }
        }

        _screenStateReadModel.InvalidateUiReadCaches();

        return new TypePinResult(digits.Length, validatedPerDigitDelayMs);
    }

    public async Task<AssertTextInputReadyResult> AssertTextInputReadyAsync(bool requireKeyboard, int timeoutSec)
    {
        var validatedTimeoutSec = RequirePositive(timeoutSec, "assertTextInputReady requires timeoutSec greater than zero.");
        var deadline = _timeProvider.GetUtcNow().AddSeconds(validatedTimeoutSec);

        while (_timeProvider.GetUtcNow() < deadline)
        {
            var document = await _screenStateReadModel.LoadUiDocumentWithRetryAsync().ConfigureAwait(false);
            var focused = document.Descendants("node")
                .FirstOrDefault(node =>
                    ((string?)node.Attribute("class"))?.Contains("EditText", StringComparison.OrdinalIgnoreCase) is true &&
                    bool.TryParse((string?)node.Attribute("focused"), out var isFocused) &&
                    isFocused);

            var keyboardVisible = !requireKeyboard || await _screenStateReadModel.IsKeyboardVisibleAsync().ConfigureAwait(false);
            if (focused is not null && keyboardVisible)
            {
                return new AssertTextInputReadyResult(
                    requireKeyboard,
                    keyboardVisible,
                    (string?)focused.Attribute("text"),
                    (string?)focused.Attribute("resource-id"),
                    (string?)focused.Attribute("bounds"));
            }

            await _delay.DelayAsync(250).ConfigureAwait(false);
        }

        throw new TimeoutException($"Timed out after {validatedTimeoutSec}s waiting for a focused text input{(requireKeyboard ? " and visible keyboard" : string.Empty)}.");
    }

    public async Task<AssertBelowResult> AssertBelowAsync(string text, string referenceText, int maxGapPx)
    {
        var subjectText = RequireNonBlank(text, "assertBelow requires text.");
        var anchorText = RequireNonBlank(referenceText, "assertBelow requires below.");
        var validatedMaxGapPx = RequireNonNegative(maxGapPx, "assertBelow maxGapPx must be zero or greater.");
        var state = await _screenStateReadModel.CaptureScreenStateWithRetryAsync("assert-below").ConfigureAwait(false);
        var subject = FindSingleMatch(state, subjectText, "assertBelow text");
        var reference = FindSingleMatch(state, anchorText, "assertBelow reference");
        var gapPx = subject.Top - reference.Bottom;

        if (gapPx < 0 || gapPx > validatedMaxGapPx)
        {
            throw new InvalidOperationException($"Expected '{subjectText}' below '{anchorText}' within {validatedMaxGapPx}px, but gap was {gapPx}px.");
        }

        return new AssertBelowResult(subjectText, anchorText, gapPx, validatedMaxGapPx);
    }

    public async Task<AssertAlignedResult> AssertAlignedAsync(string text, string referenceText, int maxDeltaPx)
    {
        var subjectText = RequireNonBlank(text, "assertAligned requires text.");
        var anchorText = RequireNonBlank(referenceText, "assertAligned requires with.");
        var validatedMaxDeltaPx = RequireNonNegative(maxDeltaPx, "assertAligned maxDeltaPx must be zero or greater.");
        var state = await _screenStateReadModel.CaptureScreenStateWithRetryAsync("assert-aligned").ConfigureAwait(false);
        var subject = FindSingleMatch(state, subjectText, "assertAligned text");
        var reference = FindSingleMatch(state, anchorText, "assertAligned reference");
        var deltaPx = Math.Abs(subject.CenterX - reference.CenterX);

        if (deltaPx > validatedMaxDeltaPx)
        {
            throw new InvalidOperationException($"Expected '{subjectText}' aligned with '{anchorText}' within {validatedMaxDeltaPx}px, but delta was {deltaPx}px.");
        }

        return new AssertAlignedResult(subjectText, anchorText, deltaPx, validatedMaxDeltaPx);
    }

    public async Task<AssertAppVersionResult> AssertAppVersionAsync(string? packageName, int maxTopInsetPx, int maxRightInsetPx)
    {
        var validatedMaxTopInsetPx = RequireNonNegative(maxTopInsetPx, "assertAppVersion maxTopInsetPx must be zero or greater.");
        var validatedMaxRightInsetPx = RequireNonNegative(maxRightInsetPx, "assertAppVersion maxRightInsetPx must be zero or greater.");
        var activePackage = string.IsNullOrWhiteSpace(packageName)
            ? _environment.GetEnvironmentVariable(AndroidRuntimeDefaults.TargetPackageEnvironmentVariable) ?? AndroidRuntimeDefaults.DefaultTargetPackage
            : packageName;
        var packageInfo = await ShellTextAsync($"dumpsys package {ShellQuote(activePackage)} | grep -E 'versionName=|versionCode=' | head -20").ConfigureAwait(false);
        var versionNameMatch = Regex.Match(packageInfo, @"versionName=(?<value>\S+)");
        var versionCodeMatch = Regex.Match(packageInfo, @"versionCode=(?<value>\d+)");
        if (!versionNameMatch.Success || !versionCodeMatch.Success)
        {
            throw new InvalidOperationException($"Could not read version metadata for package '{activePackage}'.");
        }

        var versionName = versionNameMatch.Groups["value"].Value;
        var versionCode = versionCodeMatch.Groups["value"].Value;
        var expectedLabel = versionName.Contains($"+{versionCode}", StringComparison.Ordinal)
            ? $"v{versionName}"
            : $"v{versionName}+{versionCode}";
        var state = await _screenStateReadModel.CaptureScreenStateWithRetryAsync("assert-app-version").ConfigureAwait(false);
        var element = FindSingleMatch(state, expectedLabel, "assertAppVersion text");
        var (width, _) = await _screenStateReadModel.GetDisplaySizeAsync().ConfigureAwait(false);
        var topInset = element.Top;
        var rightInset = Math.Max(0, width - element.Right);

        if (topInset > validatedMaxTopInsetPx || rightInset > validatedMaxRightInsetPx)
        {
            throw new InvalidOperationException($"Expected version label '{expectedLabel}' near the top-right corner, but top inset was {topInset}px and right inset was {rightInset}px.");
        }

        return new AssertAppVersionResult(activePackage, expectedLabel, topInset, rightInset, validatedMaxTopInsetPx, validatedMaxRightInsetPx);
    }

    private async Task<string> ShellTextAsync(string command)
    {
        var result = await _adb.ShellAsync(command).ConfigureAwait(false);
        result.EnsureSuccess($"adb shell failed: {command}");
        return result.Stdout.Trim();
    }

    private static ScreenElement FindSingleMatch(ScreenState state, string text, string role)
    {
        var matches = state.Elements.Where(element => element.Matches(text)).ToArray();
        if (matches.Length == 0)
        {
            throw new InvalidOperationException($"No visible element matched '{text}' for {role}.");
        }

        var exactMatches = matches.Where(element =>
            string.Equals(element.Text, text, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(element.ContentDescription, text, StringComparison.OrdinalIgnoreCase)).ToArray();
        var candidates = exactMatches.Length > 0 ? exactMatches : matches;
        return candidates.Length > 1 ? throw new InvalidOperationException($"Multiple visible elements matched '{text}' for {role}.") : candidates[0];
    }

    private static string RequireNonBlank(string value, string message) => string.IsNullOrWhiteSpace(value) ? throw new UsageException(message) : value;

    private static int RequirePositive(int value, string message) => value <= 0 ? throw new UsageException(message) : value;

    private static int RequireNonNegative(int value, string message) => value < 0 ? throw new UsageException(message) : value;

    private static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
}