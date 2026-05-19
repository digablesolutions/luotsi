using Luotsi.Cli.Infrastructure.Contracts;

namespace Luotsi.Cli.Hosts.Android.View;

internal sealed class AndroidMediaProjectionConsentApprover(IAdbClient adbClient)
{
    private const int MaxAttempts = 8;
    private const string UiDumpRemotePath = "/data/local/tmp/luotsi-view-window.xml";

    private readonly IAdbClient _adbClient = adbClient ?? throw new ArgumentNullException(nameof(adbClient));

    public async Task<bool> TryApproveAsync(CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var uiXml = await DumpUiHierarchyAsync(cancellationToken).ConfigureAwait(false);
            if (uiXml is null)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (TryFindStartNowButtonCenter(uiXml, out var x, out var y))
            {
                var tap = await _adbClient.ShellAsync($"input tap {x} {y}", cancellationToken).ConfigureAwait(false);
                tap.EnsureSuccess("view helper MediaProjection consent tap failed");
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    private async Task<string?> DumpUiHierarchyAsync(CancellationToken cancellationToken)
    {
        using var dumpCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        dumpCancellation.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            var dump = await _adbClient.ShellAsync($"uiautomator dump {UiDumpRemotePath} >/dev/null && cat {UiDumpRemotePath} && rm -f {UiDumpRemotePath}", dumpCancellation.Token).ConfigureAwait(false);
            return dump.Stdout;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private static bool TryFindStartNowButtonCenter(string uiXml, out int x, out int y)
    {
        const string marker = "START NOW";
        var textIndex = uiXml.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (textIndex < 0)
        {
            x = 0;
            y = 0;
            return false;
        }

        var boundsIndex = uiXml.IndexOf("bounds=\"[", textIndex, StringComparison.OrdinalIgnoreCase);
        if (boundsIndex < 0)
        {
            x = 0;
            y = 0;
            return false;
        }

        var start = boundsIndex + "bounds=\"[".Length;
        var end = uiXml.IndexOf("]\"", start, StringComparison.Ordinal);
        if (end <= start)
        {
            x = 0;
            y = 0;
            return false;
        }

        var parts = uiXml[start..end].Split([',', ']', '['], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 4 ||
            !int.TryParse(parts[0], out var left) ||
            !int.TryParse(parts[1], out var top) ||
            !int.TryParse(parts[2], out var right) ||
            !int.TryParse(parts[3], out var bottom))
        {
            x = 0;
            y = 0;
            return false;
        }

        x = (left + right) / 2;
        y = (top + bottom) / 2;
        return true;
    }
}