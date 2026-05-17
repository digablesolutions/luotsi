using Luotsi.Cli.Errors;
using Luotsi.Cli.Hosts.Android;
using Luotsi.Cli.Infrastructure;
using Luotsi.Cli.Models;
using Luotsi.Cli.View;

namespace Luotsi.Cli.Hosts.Android.View;

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
/// <param name="PackageName">Installed Android package name.</param>
/// <param name="ConsentActivity">Component name for the MediaProjection consent activity.</param>
/// <param name="CaptureService">Component name for the MediaProjection capture service.</param>
public sealed record AndroidViewHelperPackage(
    string LocalPath,
    string RemotePath,
    string MainClass,
    string Version,
    string PackageName = AndroidRuntimeDefaults.ViewHelperPackageName,
    string ConsentActivity = AndroidRuntimeDefaults.ViewHelperConsentActivity,
    string CaptureService = AndroidRuntimeDefaults.ViewHelperCaptureService);

/// <summary>
/// Default helper package locator.
/// </summary>
public sealed class AndroidViewHelperPackageLocator(IEnvironmentVariables environment, IFileSystem fileSystem) : IAndroidViewHelperPackageLocator
{
    private readonly IEnvironmentVariables _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly ViewHostPathResolver _pathResolver = new(environment);

    /// <inheritdoc />
    public AndroidViewHelperPackage Resolve()
    {
        var localPath = _environment.GetEnvironmentVariable(AndroidRuntimeDefaults.ViewHelperPathEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(localPath))
        {
            foreach (var candidate in _pathResolver.GetRepositoryRelativeFileCandidates(AndroidRuntimeDefaults.DefaultViewHelperRelativePath))
            {
                if (_fileSystem.FileExists(candidate))
                {
                    localPath = candidate;
                    break;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(localPath) || !_fileSystem.FileExists(localPath))
        {
            throw new InvalidOperationException($"Android view helper package was not found. Set {AndroidRuntimeDefaults.ViewHelperPathEnvironmentVariable} or build the helper APK at {AndroidRuntimeDefaults.DefaultViewHelperRelativePath}");
        }

        return new AndroidViewHelperPackage(localPath, AndroidRuntimeDefaults.ViewHelperRemotePath, AndroidRuntimeDefaults.ViewHelperMainClass, AndroidRuntimeDefaults.ViewHelperVersion);
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
        return await InstallAsync(package, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Installs a previously resolved helper package.
    /// </summary>
    /// <param name="package">Resolved helper package.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Installed helper metadata.</returns>
    public async Task<AndroidViewHelperPackage> InstallAsync(AndroidViewHelperPackage package, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);

        var result = await _adbClient.RunAsync(["install", "-r", package.LocalPath], cancellationToken).ConfigureAwait(false);
        result.EnsureSuccess("view helper install failed");
        return package;
    }

    /// <summary>
    /// Pushes the helper APK for the legacy app_process screenrecord entry point.
    /// </summary>
    public async Task<AndroidViewHelperPackage> PushForAppProcessAsync(AndroidViewHelperPackage package, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);

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
/// Android adb-forward transport bootstrap for the built-in mirror.
/// </summary>
public sealed class AndroidViewBootstrap(
    IAdbClientFactory adbClientFactory,
    IProcessRunner processRunner,
    IAndroidViewHelperPackageLocator packageLocator,
    IUniqueIdGenerator idGenerator) : IViewTransportBootstrap
{
    private readonly IAdbClientFactory _adbClientFactory = adbClientFactory ?? throw new ArgumentNullException(nameof(adbClientFactory));
    private readonly IProcessRunner _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    private readonly IAndroidViewHelperPackageLocator _packageLocator = packageLocator ?? throw new ArgumentNullException(nameof(packageLocator));
    private readonly IUniqueIdGenerator _idGenerator = idGenerator ?? throw new ArgumentNullException(nameof(idGenerator));

    private IAdbClient? _adbClient;
    private AndroidViewHelperPackage? _installedPackage;
    private string? _socketName;
    private int? _localPort;
    private bool _installedAppLaunch;
    private IAsyncDisposable? _screenrecordShell;

    /// <inheritdoc />
    public async Task<ViewConnectionInfo> StartAsync(ViewStartRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!string.Equals(request.Codec, "h264", StringComparison.OrdinalIgnoreCase))
        {
            throw new UsageException("The Android view helper currently supports only --codec h264.");
        }

        var requestedBackend = NormalizeCaptureBackend(request.CaptureBackend);
        var activeBackend = string.Equals(requestedBackend, ViewCaptureBackends.Screenrecord, StringComparison.Ordinal)
            ? ViewCaptureBackends.Screenrecord
            : ViewCaptureBackends.MediaProjection;

        var sessionId = _idGenerator.NewId();
        var socketName = $"{AndroidRuntimeDefaults.ViewSocketPrefix}{sessionId}";
        var adbClient = _adbClientFactory.Create(request.AdbExecutable, request.DeviceSelector, _processRunner);
        var installer = new AndroidViewServerInstaller(adbClient, _packageLocator);
        _adbClient = adbClient;
        _socketName = socketName;

        try
        {
            var package = _packageLocator.Resolve();
            _installedPackage = package;

            if (string.Equals(activeBackend, ViewCaptureBackends.MediaProjection, StringComparison.Ordinal))
            {
                await installer.InstallAsync(package, cancellationToken).ConfigureAwait(false);
                _installedAppLaunch = true;
            }
            else
            {
                await installer.PushForAppProcessAsync(package, cancellationToken).ConfigureAwait(false);
            }

            var forward = await adbClient.RunAsync(["forward", "tcp:0", $"localabstract:{socketName}"], cancellationToken).ConfigureAwait(false);
            forward.EnsureSuccess("view transport forward failed");
            var localPort = await ResolveForwardedLocalPortAsync(adbClient, forward, socketName, cancellationToken).ConfigureAwait(false);
            _localPort = localPort;

            if (string.Equals(activeBackend, ViewCaptureBackends.MediaProjection, StringComparison.Ordinal))
            {
                var start = await adbClient.RunAsync([
                    "shell",
                    "am",
                    "start",
                    "-n",
                    package.ConsentActivity,
                    "--es",
                    "socket",
                    socketName,
                    "--es",
                    "codec",
                    request.Codec,
                    "--ei",
                    "max_size",
                    request.MaxSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "--ei",
                    "max_fps",
                    request.MaxFps.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "--es",
                    "video_bit_rate",
                    request.VideoBitRate
                ], cancellationToken).ConfigureAwait(false);
                start.EnsureSuccess("view helper activity start failed");
                await TryApproveMediaProjectionConsentAsync(adbClient, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var shellCommand = $"CLASSPATH={package.RemotePath} app_process / {package.MainClass} --socket {socketName} --codec {request.Codec} --max-size {request.MaxSize} --max-fps {request.MaxFps} --video-bit-rate {request.VideoBitRate}";
                _screenrecordShell = await adbClient.StartShellAsync(shellCommand, cancellationToken).ConfigureAwait(false);
            }

            return new ViewConnectionInfo(
                sessionId,
                request.Codec,
                ViewTransportConstants.CurrentProtocolVersion,
                0,
                0,
                localPort,
                package.Version,
                ViewTransportConstants.AdbForwardTransport,
                CaptureBackend: activeBackend);
        }
        catch
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private static string NormalizeCaptureBackend(string? captureBackend)
    {
        if (string.IsNullOrWhiteSpace(captureBackend))
        {
            return ViewCaptureBackends.Auto;
        }

        return captureBackend.Trim().ToLowerInvariant() switch
        {
            ViewCaptureBackends.Auto => ViewCaptureBackends.Auto,
            ViewCaptureBackends.Screenrecord => ViewCaptureBackends.Screenrecord,
            ViewCaptureBackends.MediaProjection => ViewCaptureBackends.MediaProjection,
            _ => throw new UsageException("The Android view helper supports --capture-backend auto, screenrecord, or mediaprojection.")
        };
    }

    private static async Task TryApproveMediaProjectionConsentAsync(IAdbClient adbClient, CancellationToken cancellationToken)
    {
        const int maxAttempts = 20;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var uiXml = await DumpUiHierarchyAsync(adbClient, cancellationToken).ConfigureAwait(false);
            if (TryFindStartNowButtonCenter(uiXml, out var x, out var y))
            {
                var tap = await adbClient.ShellAsync($"input tap {x} {y}", cancellationToken).ConfigureAwait(false);
                tap.EnsureSuccess("view helper MediaProjection consent tap failed");
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<string> DumpUiHierarchyAsync(IAdbClient adbClient, CancellationToken cancellationToken)
    {
        const string remotePath = "/data/local/tmp/luotsi-view-window.xml";
        var dump = await adbClient.ShellAsync($"uiautomator dump {remotePath} >/dev/null && cat {remotePath} && rm -f {remotePath}", cancellationToken).ConfigureAwait(false);
        return dump.Stdout;
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

    private static async Task<int> ResolveForwardedLocalPortAsync(
        IAdbClient adbClient,
        AdbCommandResult forward,
        string socketName,
        CancellationToken cancellationToken)
    {
        if (TryParseTcpPort(forward.Stdout, out var localPort))
        {
            return localPort;
        }

        var list = await adbClient.RunAsync(["forward", "--list"], cancellationToken).ConfigureAwait(false);
        list.EnsureSuccess("view transport forward list failed");
        if (TryParseForwardListLocalPort(list.Stdout, socketName, out localPort))
        {
            return localPort;
        }

        throw new InvalidOperationException($"view transport forward did not return a valid local TCP port for localabstract:{socketName}.");
    }

    private static bool TryParseTcpPort(string value, out int localPort)
    {
        var stdout = value.Trim();
        return int.TryParse(stdout, out localPort) && localPort > 0;
    }

    private static bool TryParseForwardListLocalPort(string stdout, string socketName, out int localPort)
    {
        foreach (var rawLine in stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var tokens = rawLine.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tokens.Length < 3)
            {
                continue;
            }

            if (!tokens.Contains($"localabstract:{socketName}", StringComparer.Ordinal))
            {
                continue;
            }

            var localSpec = tokens.FirstOrDefault(static token => token.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase));
            if (localSpec is null)
            {
                continue;
            }

            if (TryParseTcpPort(localSpec[4..], out localPort))
            {
                return true;
            }
        }

        localPort = 0;
        return false;
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
            if (_screenrecordShell is not null)
            {
                await _screenrecordShell.DisposeAsync().ConfigureAwait(false);
            }

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
                if (_installedAppLaunch)
                {
                    await _adbClient.RunAsync(["shell", "am", "force-stop", _installedPackage.PackageName], cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await new AndroidViewServerInstaller(_adbClient, _packageLocator).RemoveAsync(_installedPackage.RemotePath, cancellationToken).ConfigureAwait(false);
                }
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
            _installedAppLaunch = false;
            _screenrecordShell = null;
        }
    }
}
