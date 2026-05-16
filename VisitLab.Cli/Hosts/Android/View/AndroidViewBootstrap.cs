using VisitLab.Cli.Errors;
using VisitLab.Cli.Infrastructure;
using VisitLab.Cli.Models;
using VisitLab.Cli.View;

namespace VisitLab.Cli.Hosts.Android.View;

/// <summary>
/// Locates the packaged Android view helper.
/// </summary>
public interface IAndroidViewHelperPackageLocator
{
    /// <summary>
    /// Resolves the helper package to install on the device.
    /// </summary>
    /// <returns>Resolved helper package.</returns>
    AndroidViewHelperPackage Resolve();
}

/// <summary>
/// Android helper package metadata.
/// </summary>
/// <param name="LocalPath">Host-local package path.</param>
/// <param name="RemotePath">Remote installation path.</param>
/// <param name="MainClass">App process entry point.</param>
/// <param name="Version">Helper version string.</param>
public sealed record AndroidViewHelperPackage(string LocalPath, string RemotePath, string MainClass, string Version);

/// <summary>
/// Default helper package locator.
/// </summary>
public sealed class AndroidViewHelperPackageLocator(IEnvironmentVariables environment, IFileSystem fileSystem) : IAndroidViewHelperPackageLocator
{
    private readonly IEnvironmentVariables _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private const string DefaultHelperRelativePath = "VisitLab.ViewServer.Android\\app\\build\\outputs\\apk\\debug\\app-debug.apk";

    /// <inheritdoc />
    public AndroidViewHelperPackage Resolve()
    {
        var localPath = _environment.GetEnvironmentVariable("DEVICE_E2E_VIEW_HELPER_JAR");
        if (string.IsNullOrWhiteSpace(localPath))
        {
            var currentDirectoryCandidate = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), DefaultHelperRelativePath));
            if (_fileSystem.FileExists(currentDirectoryCandidate))
            {
                localPath = currentDirectoryCandidate;
            }
            else
            {
                var appBaseCandidate = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", DefaultHelperRelativePath));
                if (_fileSystem.FileExists(appBaseCandidate))
                {
                    localPath = appBaseCandidate;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(localPath) || !_fileSystem.FileExists(localPath))
        {
            throw new InvalidOperationException($"Android view helper package was not found. Set DEVICE_E2E_VIEW_HELPER_JAR or build the helper APK at {DefaultHelperRelativePath}");
        }

        return new AndroidViewHelperPackage(localPath, "/data/local/tmp/visitlab-view-server.apk", "fi.systam.visitlab.view.Main", "phase-3-screenrecord");
    }
}

/// <summary>
/// Installs the Android view helper on the device.
/// </summary>
public sealed class AndroidViewServerInstaller(IAdbClient adbClient, IAndroidViewHelperPackageLocator packageLocator)
{
    private readonly IAdbClient _adbClient = adbClient ?? throw new ArgumentNullException(nameof(adbClient));
    private readonly IAndroidViewHelperPackageLocator _packageLocator = packageLocator ?? throw new ArgumentNullException(nameof(packageLocator));

    /// <summary>
    /// Resolves and installs the helper package.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Installed helper metadata.</returns>
    public async Task<AndroidViewHelperPackage> InstallAsync(CancellationToken cancellationToken = default)
    {
        var package = _packageLocator.Resolve();
        var result = await _adbClient.RunAsync(["push", package.LocalPath, package.RemotePath], cancellationToken).ConfigureAwait(false);
        result.EnsureSuccess("view helper push failed");
        return package;
    }

    /// <summary>
    /// Removes the helper package from the device.
    /// </summary>
    /// <param name="remotePath">Remote path to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Completion task.</returns>
    public async Task RemoveAsync(string remotePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(remotePath))
        {
            return;
        }

        await _adbClient.ShellAsync($"rm -f {remotePath}", cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Phase 2 Android transport bootstrap for the built-in mirror.
/// </summary>
public sealed class AndroidViewBootstrap(
    IAdbClientFactory adbClientFactory,
    IProcessRunner processRunner,
    IAndroidViewHelperPackageLocator packageLocator,
    IUniqueIdGenerator idGenerator) : IViewTransportBootstrap
{
    private const int DefaultLocalPort = 27183;

    private readonly IAdbClientFactory _adbClientFactory = adbClientFactory ?? throw new ArgumentNullException(nameof(adbClientFactory));
    private readonly IProcessRunner _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    private readonly IAndroidViewHelperPackageLocator _packageLocator = packageLocator ?? throw new ArgumentNullException(nameof(packageLocator));
    private readonly IUniqueIdGenerator _idGenerator = idGenerator ?? throw new ArgumentNullException(nameof(idGenerator));

    private IAdbClient? _adbClient;
    private AndroidViewHelperPackage? _installedPackage;
    private string? _socketName;
    private int? _localPort;

    /// <inheritdoc />
    public async Task<ViewConnectionInfo> StartAsync(ViewStartRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!string.Equals(request.Codec, "h264", StringComparison.OrdinalIgnoreCase))
        {
            throw new UsageException("The Android view helper currently supports only --codec h264.");
        }

        var sessionId = _idGenerator.NewId();
        var socketName = $"visitlab_view_{sessionId}";
        var localPort = DefaultLocalPort;
        var adbClient = _adbClientFactory.Create(request.AdbExecutable, request.DeviceSelector, _processRunner);
        var installer = new AndroidViewServerInstaller(adbClient, _packageLocator);
        var package = await installer.InstallAsync(cancellationToken).ConfigureAwait(false);

        var forward = await adbClient.RunAsync(["forward", $"tcp:{localPort}", $"localabstract:{socketName}"], cancellationToken).ConfigureAwait(false);
        forward.EnsureSuccess("view transport forward failed");

        var shellCommand = $"sh -c 'CLASSPATH={package.RemotePath} app_process / {package.MainClass} --socket {socketName} --codec {request.Codec} --max-size {request.MaxSize} --max-fps {request.MaxFps} --video-bit-rate {request.VideoBitRate} >/dev/null 2>&1 &'";
        var start = await adbClient.ShellAsync(shellCommand, cancellationToken).ConfigureAwait(false);
        start.EnsureSuccess("view helper start failed");

        _adbClient = adbClient;
        _installedPackage = package;
        _socketName = socketName;
        _localPort = localPort;

        return new ViewConnectionInfo(sessionId, request.Codec, ViewPacketStreamReader.CurrentProtocolVersion, 0, 0, localPort, package.Version, "adb-forward");
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_adbClient is null)
        {
            return;
        }

        try
        {
            if (_localPort.HasValue)
            {
                await _adbClient.RunAsync(["forward", "--remove", $"tcp:{_localPort.Value}"], cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
        }

        try
        {
            if (_socketName is not null)
            {
                await _adbClient.ShellAsync($"pkill -f {_socketName}", cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
        }

        try
        {
            if (_installedPackage is not null)
            {
                await new AndroidViewServerInstaller(_adbClient, _packageLocator).RemoveAsync(_installedPackage.RemotePath, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
        }
        finally
        {
            _adbClient = null;
            _installedPackage = null;
            _socketName = null;
            _localPort = null;
        }
    }
}