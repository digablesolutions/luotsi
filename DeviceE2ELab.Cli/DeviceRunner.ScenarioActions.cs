using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace DeviceE2ELab.Cli;

public sealed partial class DeviceRunner
{
    private const string DefaultKioskPackage = "fi.systam.visit";

    public async Task<object> WaitNotVisibleAsync(string text, int timeoutSec)
    {
        var deadline = _timeProvider.GetUtcNow().AddSeconds(Math.Max(1, timeoutSec));
        var attempt = 0;

        while (_timeProvider.GetUtcNow() < deadline)
        {
            attempt++;
            var state = await CaptureScreenStateAsync($"wait-not-visible-{attempt:000}").ConfigureAwait(false);
            if (!state.Elements.Any(element => element.Matches(text)))
            {
                return new { text, attempt_count = attempt, visible = false };
            }

            await _delay.DelayAsync(500).ConfigureAwait(false);
        }

        throw new TimeoutException($"Timed out after {timeoutSec}s waiting for text '{text}' to disappear.");
    }

    public async Task<object> TapPointAsync(string? label, int? x, int? y, double? xRatio, double? yRatio, int postTapDelayMs)
    {
        if (x.HasValue != y.HasValue)
        {
            throw new UsageException("tapPoint requires both x and y when using absolute coordinates.");
        }

        var (resolvedX, resolvedY) = x.HasValue && y.HasValue
            ? (x.Value, y.Value)
            : await ResolveRelativePointAsync(xRatio, yRatio).ConfigureAwait(false);

        var result = await _adb.ShellAsync($"input tap {resolvedX} {resolvedY}").ConfigureAwait(false);
        result.EnsureSuccess("tap point failed");

        if (postTapDelayMs > 0)
        {
            await _delay.DelayAsync(postTapDelayMs).ConfigureAwait(false);
        }

        return new
        {
            label,
            x = resolvedX,
            y = resolvedY,
            x_ratio = xRatio,
            y_ratio = yRatio,
            post_tap_delay_ms = Math.Max(0, postTapDelayMs),
        };
    }

    public async Task<object> DoubleTapHeaderLogoAsync()
    {
        var (x, y) = await ResolveHeaderLogoTargetAsync().ConfigureAwait(false);
        foreach (var _ in Enumerable.Range(0, 2))
        {
            var tap = await _adb.ShellAsync($"input tap {x} {y}").ConfigureAwait(false);
            tap.EnsureSuccess("double tap header logo failed");
            await _delay.DelayAsync(160).ConfigureAwait(false);
        }

        return new { target = "header_logo", x, y, interval_ms = 160 };
    }

    public async Task<object> TypePinAsync(string pin, int perDigitDelayMs)
    {
        if (string.IsNullOrWhiteSpace(pin))
        {
            throw new UsageException("typePin requires text.");
        }

        var digits = pin.Trim();
        foreach (var digit in digits)
        {
            if (!char.IsDigit(digit))
            {
                throw new UsageException($"typePin supports digits only. Invalid character '{digit}'.");
            }

            var result = await _adb.ShellAsync($"input keyevent KEYCODE_{digit}").ConfigureAwait(false);
            result.EnsureSuccess("type pin failed");

            if (perDigitDelayMs > 0)
            {
                await _delay.DelayAsync(perDigitDelayMs).ConfigureAwait(false);
            }
        }

        return new { pin_length = digits.Length, per_digit_delay_ms = Math.Max(0, perDigitDelayMs) };
    }

    public async Task<object> ResetLogAsync()
    {
        var result = await _adb.RunAsync(["logcat", "-c"]).ConfigureAwait(false);
        result.EnsureSuccess("log reset failed");
        return new { cleared = true };
    }

    public async Task<object> AssertEventAsync(string name, IReadOnlyList<string> contains, string? detailsPattern, int timeoutSec)
    {
        var started = _timeProvider.GetUtcNow();
        var deadline = started.AddSeconds(Math.Max(1, timeoutSec));
        string? invocation = null;
        string lastLog = string.Empty;
        string? matchedLine = null;
        var detailsRegex = string.IsNullOrWhiteSpace(detailsPattern) ? null : new Regex(detailsPattern, RegexOptions.IgnoreCase);

        while (_timeProvider.GetUtcNow() < deadline)
        {
            var result = await _adb.RunAsync([
                "logcat",
                "-d",
                "-v",
                "brief",
                "-T",
                started.ToLocalTime().ToString("MM-dd HH:mm:ss.fff"),
                "*:V"]).ConfigureAwait(false);
            result.EnsureSuccess("assert event failed");
            invocation = result.Invocation;
            lastLog = result.Stdout;
            matchedLine = lastLog.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(static line => line.TrimEnd('\r'))
                .LastOrDefault(line => EventLineMatches(line, name, contains, detailsRegex));

            if (matchedLine is not null)
            {
                break;
            }

            await _delay.DelayAsync(250).ConfigureAwait(false);
        }

        await _artifacts.WriteTextAsync("assert-event.txt", lastLog).ConfigureAwait(false);
        await _artifacts.WriteJsonAsync(
            "assert-event.json",
            new
            {
                schema = "device-e2e-lab-assert-event.v1",
                name,
                contains,
                details_pattern = detailsPattern,
                timeout_sec = timeoutSec,
                invocation,
                matched_line = matchedLine,
            }).ConfigureAwait(false);

        if (matchedLine is null)
        {
            throw new SemanticWaitTimeoutException($"event '{name}'", timeoutSec);
        }

        return new { name, contains, details_pattern = detailsPattern, matched_line = matchedLine };
    }

    public async Task<object> TakeScreenshotAsync(string label)
    {
        var fileName = $"{Slugify(label)}-screenshot.png";
        await CaptureScreenshotAsync(fileName).ConfigureAwait(false);
        return new { label, file = fileName };
    }

    public async Task<object> CaptureArtifactsAsync(string label)
    {
        var slug = Slugify(label);
        var screenshot = $"{slug}-screenshot.png";
        var logcat = $"{slug}-logcat.txt";
        await CaptureScreenshotAsync(screenshot).ConfigureAwait(false);
        await CaptureLogcatSnapshotAsync(logcat, 500).ConfigureAwait(false);
        await CaptureScreenStateAsync(slug).ConfigureAwait(false);
        return new
        {
            label,
            screenshot,
            logcat,
            screen_state = $"{slug}-screen-state.json",
            hierarchy = $"{slug}-hierarchy.xml",
        };
    }

    public async Task<object> AssertTextInputReadyAsync(bool requireKeyboard, int timeoutSec)
    {
        var deadline = _timeProvider.GetUtcNow().AddSeconds(Math.Max(1, timeoutSec));

        while (_timeProvider.GetUtcNow() < deadline)
        {
            var document = await LoadUiDocumentAsync().ConfigureAwait(false);
            var focused = document.Descendants("node")
                .FirstOrDefault(node =>
                    ((string?)node.Attribute("class"))?.Contains("EditText", StringComparison.OrdinalIgnoreCase) is true &&
                    bool.TryParse((string?)node.Attribute("focused"), out var isFocused) &&
                    isFocused);

            var keyboardVisible = !requireKeyboard || await IsKeyboardVisibleAsync().ConfigureAwait(false);
            if (focused is not null && keyboardVisible)
            {
                return new
                {
                    require_keyboard = requireKeyboard,
                    keyboard_visible = keyboardVisible,
                    text = (string?)focused.Attribute("text"),
                    resource_id = (string?)focused.Attribute("resource-id"),
                    bounds = (string?)focused.Attribute("bounds"),
                };
            }

            await _delay.DelayAsync(250).ConfigureAwait(false);
        }

        throw new TimeoutException($"Timed out after {timeoutSec}s waiting for a focused text input{(requireKeyboard ? " and visible keyboard" : string.Empty)}.");
    }

    public async Task<object> AssertBelowAsync(string text, string referenceText, int maxGapPx)
    {
        var state = await CaptureScreenStateAsync("assert-below").ConfigureAwait(false);
        var subject = FindSingleMatch(state, text, "assertBelow text");
        var reference = FindSingleMatch(state, referenceText, "assertBelow reference");
        var gapPx = subject.Top - reference.Bottom;

        if (gapPx < 0 || gapPx > maxGapPx)
        {
            throw new InvalidOperationException($"Expected '{text}' below '{referenceText}' within {maxGapPx}px, but gap was {gapPx}px.");
        }

        return new { text, below = referenceText, gap_px = gapPx, max_gap_px = maxGapPx };
    }

    public async Task<object> AssertAlignedAsync(string text, string referenceText, int maxDeltaPx)
    {
        var state = await CaptureScreenStateAsync("assert-aligned").ConfigureAwait(false);
        var subject = FindSingleMatch(state, text, "assertAligned text");
        var reference = FindSingleMatch(state, referenceText, "assertAligned reference");
        var deltaPx = Math.Abs(subject.CenterX - reference.CenterX);

        if (deltaPx > maxDeltaPx)
        {
            throw new InvalidOperationException($"Expected '{text}' aligned with '{referenceText}' within {maxDeltaPx}px, but delta was {deltaPx}px.");
        }

        return new { text, with = referenceText, delta_px = deltaPx, max_delta_px = maxDeltaPx };
    }

    public async Task<object> AssertAppVersionAsync(string? packageName, int maxTopInsetPx, int maxRightInsetPx)
    {
        var activePackage = string.IsNullOrWhiteSpace(packageName)
            ? _environment.GetEnvironmentVariable("DEVICE_TEST_PACKAGE") ?? DefaultKioskPackage
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
        var state = await CaptureScreenStateAsync("assert-app-version").ConfigureAwait(false);
        var element = FindSingleMatch(state, expectedLabel, "assertAppVersion text");
        var (width, _) = await GetDisplaySizeAsync().ConfigureAwait(false);
        var topInset = element.Top;
        var rightInset = Math.Max(0, width - element.Right);

        if (topInset > maxTopInsetPx || rightInset > maxRightInsetPx)
        {
            throw new InvalidOperationException($"Expected version label '{expectedLabel}' near the top-right corner, but top inset was {topInset}px and right inset was {rightInset}px.");
        }

        return new
        {
            package = activePackage,
            label = expectedLabel,
            top_inset_px = topInset,
            right_inset_px = rightInset,
            max_top_inset_px = maxTopInsetPx,
            max_right_inset_px = maxRightInsetPx,
        };
    }

    private async Task<bool> IsKeyboardVisibleAsync()
    {
        var result = await ShellTextAsync("dumpsys input_method | grep -E 'mInputShown=true|mIsInputViewShown=true|mShowRequested=true' | head -1").ConfigureAwait(false);
        return !string.IsNullOrWhiteSpace(result);
    }

    private async Task<(int X, int Y)> ResolveRelativePointAsync(double? xRatio, double? yRatio)
    {
        if (!xRatio.HasValue || !yRatio.HasValue)
        {
            throw new UsageException("tapPoint requires either x/y or xRatio/yRatio.");
        }

        var (width, height) = await GetDisplaySizeAsync().ConfigureAwait(false);
        var resolvedX = (int)Math.Round(width * xRatio.Value, MidpointRounding.AwayFromZero);
        var resolvedY = (int)Math.Round(height * yRatio.Value, MidpointRounding.AwayFromZero);
        return (resolvedX, resolvedY);
    }

    private async Task<(int Width, int Height)> GetDisplaySizeAsync()
    {
        var text = await ShellTextAsync("wm size").ConfigureAwait(false);
        var match = Regex.Match(text, @"(?<width>\d+)x(?<height>\d+)");
        if (!match.Success)
        {
            throw new InvalidOperationException($"Could not parse device display size from '{text}'.");
        }

        return (
            int.Parse(match.Groups["width"].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups["height"].Value, CultureInfo.InvariantCulture));
    }

    private async Task<(int X, int Y)> ResolveHeaderLogoTargetAsync()
    {
        var document = await LoadUiDocumentAsync().ConfigureAwait(false);
        var (width, _) = await GetDisplaySizeAsync().ConfigureAwait(false);
        var centerX = width / 2d;

        var candidates = document.Descendants("node")
            .Where(node => string.Equals((string?)node.Attribute("class"), "android.widget.ImageView", StringComparison.Ordinal))
            .Select(node => ParseNodeBounds((string?)node.Attribute("bounds") ?? "[0,0][0,0]"))
            .Where(bounds => bounds.Top <= 140)
            .Select(bounds => new
            {
                Bounds = bounds,
                Delta = Math.Abs(((bounds.Left + bounds.Right) / 2d) - centerX),
            })
            .Where(candidate => candidate.Delta <= width * 0.2)
            .OrderBy(candidate => candidate.Delta)
            .ThenBy(candidate => candidate.Bounds.Top)
            .ToArray();

        var match = candidates.FirstOrDefault() ?? throw new InvalidOperationException("Could not find a header logo target near the top center of the screen.");
        return (((match.Bounds.Left + match.Bounds.Right) / 2), ((match.Bounds.Top + match.Bounds.Bottom) / 2));
    }

    private async Task<XDocument> LoadUiDocumentAsync()
    {
        var xml = await DumpUiAsync().ConfigureAwait(false);
        try
        {
            return XDocument.Parse(xml);
        }
        catch (Exception ex) when (ex is XmlException || ex is InvalidOperationException)
        {
            throw new InvalidOperationException("UI hierarchy dump was empty or invalid XML.", ex);
        }
    }

    private static bool EventLineMatches(string line, string name, IReadOnlyList<string> contains, Regex? detailsRegex)
    {
        if (!line.Contains($"Log.{name}", StringComparison.OrdinalIgnoreCase) &&
            !line.Contains(name, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (contains.Any(required => !line.Contains(required, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return detailsRegex is null || detailsRegex.IsMatch(line);
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
        if (candidates.Length > 1)
        {
            throw new InvalidOperationException($"Multiple visible elements matched '{text}' for {role}.");
        }

        return candidates[0];
    }

    private static Bounds ParseNodeBounds(string value)
    {
        var numbers = value.Split(new[] { '[', ']', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(static part => int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0)
            .ToArray();
        return numbers.Length >= 4 ? new Bounds(numbers[0], numbers[1], numbers[2], numbers[3]) : new Bounds(0, 0, 0, 0);
    }
}
