using System.Xml.Linq;
using Luotsi.Cli.Infrastructure.Contracts;

namespace Luotsi.Cli.Hosts.Android.View;

internal sealed class AndroidMediaProjectionConsentApprover(IAdbClient adbClient)
{
    private const int MaxAttempts = 40;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(250);
    private const string UiDumpRemotePath = "/data/local/tmp/luotsi-view-window.xml";
    private const string MediaProjectionPermissionActivity = "MediaProjectionPermissionActivity";

    private readonly IAdbClient _adbClient = adbClient ?? throw new ArgumentNullException(nameof(adbClient));

    public async Task<bool> TryApproveAsync(CancellationToken cancellationToken = default)
    {
        var tappedApproval = false;
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var uiXml = await DumpUiHierarchyAsync(cancellationToken).ConfigureAwait(false);
            if (uiXml is null)
            {
                if (await TryTapFocusedMediaProjectionPromptAsync(cancellationToken).ConfigureAwait(false))
                {
                    tappedApproval = true;
                }

                await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (TryFindApproveButtonCenter(uiXml, out var x, out var y))
            {
                var tap = await _adbClient.ShellAsync($"input tap {x} {y}", cancellationToken).ConfigureAwait(false);
                tap.EnsureSuccess("view helper MediaProjection consent tap failed");
                tappedApproval = true;
                await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (string.IsNullOrWhiteSpace(uiXml) &&
                await TryTapFocusedMediaProjectionPromptAsync(cancellationToken).ConfigureAwait(false))
            {
                tappedApproval = true;
                await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (tappedApproval)
            {
                var promptFocused = await TryGetMediaProjectionPromptFocusedAsync(cancellationToken).ConfigureAwait(false);
                if (promptFocused == false)
                {
                    return true;
                }

                if (promptFocused == true &&
                    string.IsNullOrWhiteSpace(uiXml) &&
                    await TryTapFocusedMediaProjectionPromptAsync(cancellationToken).ConfigureAwait(false))
                {
                    await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
                    continue;
                }
            }

            await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    private async Task<string?> DumpUiHierarchyAsync(CancellationToken cancellationToken)
    {
        using var dumpCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        dumpCancellation.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            var dump = await _adbClient.ShellAsync(BuildBoundedUiDumpCommand(), dumpCancellation.Token).ConfigureAwait(false);
            return dump.Stdout;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private async Task<bool?> TryGetMediaProjectionPromptFocusedAsync(CancellationToken cancellationToken)
    {
        var focus = await _adbClient.ShellAsync("dumpsys window | grep -E 'mCurrentFocus|mFocusedApp|mResumedActivity' | head -3", cancellationToken).ConfigureAwait(false);
        if (focus.ExitCode != 0)
        {
            return null;
        }

        return focus.Stdout.Contains(MediaProjectionPermissionActivity, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> TryTapFocusedMediaProjectionPromptAsync(CancellationToken cancellationToken)
    {
        if (await TryGetMediaProjectionPromptFocusedAsync(cancellationToken).ConfigureAwait(false) != true)
        {
            return false;
        }

        var size = await _adbClient.ShellAsync("wm size", cancellationToken).ConfigureAwait(false);
        if (size.ExitCode != 0 || !TryParseDisplaySize(size.Stdout, out var width, out var height))
        {
            return false;
        }

        var x = width * 2 / 3;
        var y = height * 31 / 50;
        var tap = await _adbClient.ShellAsync($"input tap {x} {y}", cancellationToken).ConfigureAwait(false);
        tap.EnsureSuccess("view helper MediaProjection consent fallback tap failed");
        return true;
    }

    private static string BuildBoundedUiDumpCommand() =>
        $"rm -f {UiDumpRemotePath}; " +
        $"(uiautomator dump {UiDumpRemotePath} >/dev/null 2>&1) & " +
        "dump_pid=$!; sleep 1; " +
        "if kill -0 $dump_pid 2>/dev/null; then kill -9 $dump_pid 2>/dev/null; fi; " +
        $"cat {UiDumpRemotePath} 2>/dev/null; rm -f {UiDumpRemotePath}";

    private static bool TryParseDisplaySize(string output, out int width, out int height)
    {
        foreach (var rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var markerIndex = rawLine.IndexOf("size:", StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
            {
                continue;
            }

            var value = rawLine[(markerIndex + "size:".Length)..].Trim();
            var parts = value.Split('x', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 2 &&
                int.TryParse(parts[0], out width) &&
                int.TryParse(parts[1], out height) &&
                width > 0 &&
                height > 0)
            {
                return true;
            }
        }

        width = 0;
        height = 0;
        return false;
    }

    private static bool TryFindApproveButtonCenter(string uiXml, out int x, out int y)
    {
        x = 0;
        y = 0;

        XDocument document;
        try
        {
            document = XDocument.Parse(uiXml);
        }
        catch
        {
            return TryFindApproveButtonCenterByText(uiXml, out x, out y);
        }

        foreach (var node in document.Descendants("node"))
        {
            var text = (string?)node.Attribute("text") ?? string.Empty;
            var resourceId = (string?)node.Attribute("resource-id") ?? string.Empty;
            if (!IsApprovalButton(text, resourceId))
            {
                continue;
            }

            var bounds = (string?)node.Attribute("bounds") ?? string.Empty;
            if (TryParseBoundsCenter(bounds, out x, out y))
            {
                return true;
            }
        }

        return TryFindApproveButtonCenterByText(uiXml, out x, out y);
    }

    private static bool IsApprovalButton(string text, string resourceId)
    {
        if (string.Equals(resourceId, "android:id/button1", StringComparison.Ordinal) &&
            !text.Contains("cancel", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return text.Contains("START NOW", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("START RECORDING", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("START CASTING", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseBoundsCenter(string bounds, out int x, out int y)
    {
        x = 0;
        y = 0;
        if (string.IsNullOrWhiteSpace(bounds))
        {
            return false;
        }

        var parts = bounds.Split([',', ']', '['], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 4 ||
            !int.TryParse(parts[0], out var left) ||
            !int.TryParse(parts[1], out var top) ||
            !int.TryParse(parts[2], out var right) ||
            !int.TryParse(parts[3], out var bottom))
        {
            return false;
        }

        x = (left + right) / 2;
        y = (top + bottom) / 2;
        return true;
    }

    private static bool TryFindApproveButtonCenterByText(string uiXml, out int x, out int y)
    {
        foreach (var marker in new[] { "START NOW", "START RECORDING", "START CASTING", "android:id/button1" })
        {
            var markerIndex = uiXml.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
            {
                continue;
            }

            var boundsIndex = uiXml.IndexOf("bounds=\"[", markerIndex, StringComparison.OrdinalIgnoreCase);
            if (boundsIndex < 0)
            {
                continue;
            }

            var start = boundsIndex + "bounds=\"".Length;
            var end = uiXml.IndexOf('"', start);
            if (end <= start)
            {
                continue;
            }

            if (TryParseBoundsCenter(uiXml[start..end], out x, out y))
            {
                return true;
            }
        }

        x = 0;
        y = 0;
        return false;
    }
}
