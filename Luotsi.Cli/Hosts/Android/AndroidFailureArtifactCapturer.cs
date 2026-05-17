using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Hosts.Android;

internal sealed class AndroidFailureArtifactCapturer(
    ArtifactSession artifacts,
    TimeProvider timeProvider,
    Func<FailureCaptureRequest, string> buildPrefix,
    Func<string, Task> captureScreenshotAsync,
    Func<string, int, Task> captureLogcatSnapshotAsync,
    Func<string, Task<ScreenState>> captureScreenStateAsync)
{
    private readonly ArtifactSession _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly Func<FailureCaptureRequest, string> _buildPrefix = buildPrefix ?? throw new ArgumentNullException(nameof(buildPrefix));
    private readonly Func<string, Task> _captureScreenshotAsync = captureScreenshotAsync ?? throw new ArgumentNullException(nameof(captureScreenshotAsync));
    private readonly Func<string, int, Task> _captureLogcatSnapshotAsync = captureLogcatSnapshotAsync ?? throw new ArgumentNullException(nameof(captureLogcatSnapshotAsync));
    private readonly Func<string, Task<ScreenState>> _captureScreenStateAsync = captureScreenStateAsync ?? throw new ArgumentNullException(nameof(captureScreenStateAsync));

    public async Task<FailureArtifactBundle> CaptureAsync(FailureCaptureRequest request, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(exception);

        var prefix = _buildPrefix(request);
        var captured = new List<FailureArtifact>();
        var captureFailures = new List<FailureCaptureError>();

        await TryCaptureAsync(captured, captureFailures, "screenshot", async () =>
        {
            var fileName = DeviceArtifactNames.ScreenshotForLabel(prefix);
            await _captureScreenshotAsync(fileName).ConfigureAwait(false);
            return fileName;
        }).ConfigureAwait(false);

        await TryCaptureAsync(captured, captureFailures, "logcat", async () =>
        {
            var fileName = DeviceArtifactNames.LogcatForLabel(prefix);
            await _captureLogcatSnapshotAsync(fileName, 1000).ConfigureAwait(false);
            return fileName;
        }).ConfigureAwait(false);

        await TryCaptureAsync(captured, captureFailures, "screen-state", async () =>
        {
            await _captureScreenStateAsync(prefix).ConfigureAwait(false);
            return DeviceArtifactNames.ScreenStateForLabel(prefix);
        }).ConfigureAwait(false);

        var metadata = new FailureArtifactBundle(
            ResultSchemas.FailureBundle,
            _timeProvider.GetUtcNow(),
            request.Scope,
            request.Name,
            request.File,
            request.StepIndex,
            request.StepName,
            request.Action,
            exception.GetType().FullName ?? exception.GetType().Name,
            exception.Message,
            captured,
            captureFailures);
        var metadataFile = DeviceArtifactNames.FailureMetadataForLabel(prefix);
        await _artifacts.WriteJsonAsync(metadataFile, metadata).ConfigureAwait(false);
        return metadata with { MetadataFile = metadataFile };
    }

    private static async Task TryCaptureAsync(
        ICollection<FailureArtifact> captured,
        ICollection<FailureCaptureError> captureFailures,
        string name,
        Func<Task<string>> action)
    {
        try
        {
            captured.Add(new FailureArtifact(name, await action().ConfigureAwait(false)));
        }
        catch (Exception captureException) when (captureException is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
        {
            captureFailures.Add(new FailureCaptureError(name, captureException.Message));
        }
    }
}
