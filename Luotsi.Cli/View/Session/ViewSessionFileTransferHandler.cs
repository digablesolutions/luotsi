using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;
using Luotsi.Cli.View.Contracts;

namespace Luotsi.Cli.View.Session;

internal sealed class ViewSessionFileTransferHandler
{
    private readonly IDeviceHost _deviceHost;
    private readonly ArtifactSession _artifacts;
    private readonly TimeProvider _timeProvider;
    private readonly string _sessionId;
    private readonly Action<object> _writeEvent;
    private readonly Func<string, bool> _tryBlockReadOnly;

    public ViewSessionFileTransferHandler(
        ViewSessionInteractionContext context,
        Func<string, bool> tryBlockReadOnly)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tryBlockReadOnly);

        _deviceHost = context.DeviceHost ?? throw new ArgumentNullException(nameof(context.DeviceHost));
        _artifacts = context.Artifacts ?? throw new ArgumentNullException(nameof(context.Artifacts));
        _timeProvider = context.TimeProvider ?? throw new ArgumentNullException(nameof(context.TimeProvider));
        _sessionId = string.IsNullOrWhiteSpace(context.SessionId)
            ? throw new ArgumentException("Session id is required.", nameof(context.SessionId))
            : context.SessionId;
        _writeEvent = context.WriteEvent ?? throw new ArgumentNullException(nameof(context.WriteEvent));
        _tryBlockReadOnly = tryBlockReadOnly;
    }

    public async Task<bool> TryHandleAsync(ViewInteractionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        switch (request)
        {
            case ViewFileDropRequest fileDropRequest:
                if (_tryBlockReadOnly("file_drop"))
                {
                    return true;
                }

                await HandleFileDropAsync(fileDropRequest.FilePath).ConfigureAwait(false);
                return true;

            case ViewFilePullRequest filePullRequest:
                if (_tryBlockReadOnly("file_pull"))
                {
                    return true;
                }

                await HandleFilePullAsync(filePullRequest).ConfigureAwait(false);
                return true;

            default:
                return false;
        }
    }

    public async Task HandleFileDropAsync(string filePath)
    {
        if (string.Equals(Path.GetExtension(filePath), ".apk", StringComparison.OrdinalIgnoreCase))
        {
            var installResult = await _deviceHost.InstallPackageAsync(filePath).ConfigureAwait(false);
            WriteEvent(new
            {
                type = SessionEventTypes.View.PackageInstalled,
                session_id = _sessionId,
                occurred_at = _timeProvider.GetUtcNow(),
                package_path = installResult.PackagePath
            });
            return;
        }

        var pushResult = await _deviceHost.PushFileAsync(filePath).ConfigureAwait(false);
        WriteEvent(new
        {
            type = SessionEventTypes.View.FilePushed,
            session_id = _sessionId,
            occurred_at = _timeProvider.GetUtcNow(),
            local_path = pushResult.LocalPath,
            remote_path = pushResult.RemotePath
        });
    }

    public async Task HandleFilePullAsync(ViewFilePullRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var pullResult = await _deviceHost.PullFileAsync(request.RemotePath, request.LocalDirectory ?? _artifacts.Root).ConfigureAwait(false);
        WriteEvent(new
        {
            type = SessionEventTypes.View.FilePulled,
            session_id = _sessionId,
            occurred_at = _timeProvider.GetUtcNow(),
            remote_path = pullResult.RemotePath,
            local_path = pullResult.LocalPath
        });
    }

    private void WriteEvent(object value) => _writeEvent(value);
}