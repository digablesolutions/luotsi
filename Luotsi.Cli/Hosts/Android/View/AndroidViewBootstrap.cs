using System.Security.Cryptography;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.View;
using Luotsi.Cli.View.Contracts;
using Luotsi.Cli.View.Transport;

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
/// <param name="LocalSizeBytes">Host-local package size in bytes.</param>
/// <param name="LocalSha256">Host-local package SHA-256.</param>
/// <param name="ResolutionSource">How the package path was resolved.</param>
public sealed record AndroidViewHelperPackage(
    string LocalPath,
    string RemotePath,
    string MainClass,
    string Version,
    string PackageName = AndroidRuntimeDefaults.ViewHelperPackageName,
    string ConsentActivity = AndroidRuntimeDefaults.ViewHelperConsentActivity,
    string CaptureService = AndroidRuntimeDefaults.ViewHelperCaptureService,
    long? LocalSizeBytes = null,
    string? LocalSha256 = null,
    string ResolutionSource = "explicit");

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
        var resolutionSource = AndroidRuntimeDefaults.ViewHelperPathEnvironmentVariable;
        if (string.IsNullOrWhiteSpace(localPath))
        {
            resolutionSource = "repository_default";
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

        var normalizedPath = Path.GetFullPath(localPath);
        var packagePath = _fileSystem.FileExists(normalizedPath) ? normalizedPath : localPath;
        if (!string.Equals(Path.GetExtension(packagePath), ".apk", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Android view helper package must be an .apk file: {packagePath}");
        }

        var (sizeBytes, sha256) = ReadPackageFingerprint(packagePath);
        if (sizeBytes <= 0)
        {
            throw new InvalidOperationException($"Android view helper package is empty: {packagePath}");
        }

        return new AndroidViewHelperPackage(
            packagePath,
            AndroidRuntimeDefaults.ViewHelperRemotePath,
            AndroidRuntimeDefaults.ViewHelperMainClass,
            AndroidRuntimeDefaults.ViewHelperVersion,
            LocalSizeBytes: sizeBytes,
            LocalSha256: sha256,
            ResolutionSource: resolutionSource);
    }

    private (long SizeBytes, string Sha256) ReadPackageFingerprint(string path)
    {
        using var stream = _fileSystem.OpenRead(path);
        var sizeBytes = stream.CanSeek ? stream.Length : 0;
        var hash = SHA256.HashData(stream);
        return (sizeBytes, Convert.ToHexString(hash).ToLowerInvariant());
    }
}

/// <summary>
/// Installs the Android view helper on the device.
/// </summary>
public sealed class AndroidViewServerInstaller(
    IAdbClient adbClient,
    IAndroidViewHelperPackageLocator packageLocator,
    Action<ViewStartupPhase>? reportPhase = null)
{
    private readonly IAdbClient _adbClient = adbClient ?? throw new ArgumentNullException(nameof(adbClient));
    private readonly IAndroidViewHelperPackageLocator _packageLocator = packageLocator ?? throw new ArgumentNullException(nameof(packageLocator));
    private readonly Action<ViewStartupPhase>? _reportPhase = reportPhase;

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

        Report("helper_install", ViewStartupPhaseStatus.Started, "Installing Android view helper.", PackageDetail(package));
        try
        {
            var result = await _adbClient.RunAsync(["install", "-r", package.LocalPath], cancellationToken).ConfigureAwait(false);
            result.EnsureSuccess("view helper install failed");
            Report("helper_install", ViewStartupPhaseStatus.Succeeded, "Android view helper installed.", result.Stdout.Trim());
        }
        catch (Exception ex)
        {
            Report("helper_install", ViewStartupPhaseStatus.Failed, "Android view helper install failed.", ex.Message, "Run view setup --fix or rebuild the Android helper APK.");
            throw;
        }

        await VerifyInstalledAsync(package, cancellationToken).ConfigureAwait(false);
        return package;
    }

    /// <summary>
    /// Verifies that the installed helper exposes the expected Android components.
    /// </summary>
    public async Task VerifyInstalledAsync(AndroidViewHelperPackage package, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);

        Report("helper_verify", ViewStartupPhaseStatus.Started, "Verifying installed Android view helper components.", package.PackageName);
        try
        {
            var activity = await _adbClient.RunAsync(["shell", "cmd", "package", "resolve-activity", "--brief", package.ConsentActivity], cancellationToken).ConfigureAwait(false);
            activity.EnsureSuccess("view helper consent activity verification failed");
            if (!ContainsComponent(activity.Stdout, package.ConsentActivity))
            {
                throw new InvalidOperationException($"Installed helper does not expose {package.ConsentActivity}. The APK manifest may be stale or incomplete.");
            }

            var dump = await _adbClient.RunAsync(["shell", "pm", "dump", package.PackageName], cancellationToken).ConfigureAwait(false);
            dump.EnsureSuccess("view helper package verification failed");
            var serviceClassName = ToClassName(package.CaptureService);
            if (!dump.Stdout.Contains(serviceClassName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Installed helper does not expose {serviceClassName}. The APK manifest may be stale or incomplete.");
            }

            Report("helper_verify", ViewStartupPhaseStatus.Succeeded, "Installed Android view helper exposes required activity and service.", $"{package.ConsentActivity}; {serviceClassName}");
        }
        catch (Exception ex)
        {
            Report("helper_verify", ViewStartupPhaseStatus.Failed, "Installed Android view helper verification failed.", ex.Message, "Rebuild Luotsi.ViewServer.Android and reinstall with view setup --fix.");
            throw;
        }
    }

    /// <summary>
    /// Pushes the helper APK for the legacy app_process screenrecord entry point.
    /// </summary>
    public async Task<AndroidViewHelperPackage> PushForAppProcessAsync(AndroidViewHelperPackage package, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);

        Report("helper_push", ViewStartupPhaseStatus.Started, "Pushing Android view helper for screenrecord backend.", PackageDetail(package));
        var result = await _adbClient.RunAsync(["push", package.LocalPath, package.RemotePath], cancellationToken).ConfigureAwait(false);
        result.EnsureSuccess("view helper push failed");
        Report("helper_push", ViewStartupPhaseStatus.Succeeded, "Android view helper pushed.", result.Stdout.Trim());
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

    private void Report(string phase, string status, string summary, string? detail = null, string? recommendation = null) =>
        _reportPhase?.Invoke(new ViewStartupPhase(phase, status, summary, string.IsNullOrWhiteSpace(detail) ? null : detail, recommendation));

    private static string PackageDetail(AndroidViewHelperPackage package) =>
        $"path={package.LocalPath}; source={package.ResolutionSource}; size={package.LocalSizeBytes?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"}; sha256={package.LocalSha256 ?? "unknown"}";

    private static bool ContainsComponent(string output, string component) =>
        output.Contains(component, StringComparison.Ordinal) ||
        output.Contains(ToClassName(component), StringComparison.Ordinal);

    private static string ToClassName(string component)
    {
        var slashIndex = component.IndexOf('/', StringComparison.Ordinal);
        if (slashIndex < 0)
        {
            return component;
        }

        var packageName = component[..slashIndex];
        var className = component[(slashIndex + 1)..];
        return className.StartsWith(".", StringComparison.Ordinal)
            ? packageName + className
            : className;
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
    public async Task<ViewConnectionInfo> StartAsync(ViewStartRequest request, Action<ViewStartupPhase>? reportPhase = null, CancellationToken cancellationToken = default)
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
        var adbClient = _adbClientFactory.Create(request.AdbExecutable, request.DeviceSelector, _processRunner, request.CommandTimeout);
        var installer = new AndroidViewServerInstaller(adbClient, _packageLocator, reportPhase);
        _adbClient = adbClient;
        _socketName = socketName;

        try
        {
            Report(reportPhase, "helper_resolve", ViewStartupPhaseStatus.Started, "Resolving Android view helper package.");
            var package = _packageLocator.Resolve();
            Report(reportPhase, "helper_resolve", ViewStartupPhaseStatus.Succeeded, "Resolved Android view helper package.", $"path={package.LocalPath}; source={package.ResolutionSource}; sha256={package.LocalSha256 ?? "unknown"}");
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

            Report(reportPhase, "adb_forward", ViewStartupPhaseStatus.Started, "Creating adb forward for view stream.", $"local=tcp:0; remote=localabstract:{socketName}");
            var forward = await adbClient.RunAsync(["forward", "tcp:0", $"localabstract:{socketName}"], cancellationToken).ConfigureAwait(false);
            forward.EnsureSuccess("view transport forward failed");
            var localPort = await ResolveForwardedLocalPortAsync(adbClient, forward, socketName, cancellationToken).ConfigureAwait(false);
            Report(reportPhase, "adb_forward", ViewStartupPhaseStatus.Succeeded, "ADB forward is ready.", $"local=tcp:{localPort}; remote=localabstract:{socketName}");
            _localPort = localPort;

            if (string.Equals(activeBackend, ViewCaptureBackends.MediaProjection, StringComparison.Ordinal))
            {
                Report(reportPhase, "mediaprojection_activity", ViewStartupPhaseStatus.Started, "Starting Android MediaProjection consent activity.", package.ConsentActivity);
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
                Report(reportPhase, "mediaprojection_activity", ViewStartupPhaseStatus.Succeeded, "Android MediaProjection consent activity started.", start.Stdout.Trim());
                Report(reportPhase, "mediaprojection_consent", ViewStartupPhaseStatus.Started, "Waiting for Android MediaProjection consent prompt.", "uiautomator=start-now");
                var approved = await TryApproveMediaProjectionConsentAsync(adbClient, cancellationToken).ConfigureAwait(false);
                if (!approved)
                {
                    var message = "MediaProjection consent prompt was not approved or could not be detected.";
                    Report(reportPhase, "mediaprojection_consent", ViewStartupPhaseStatus.Failed, message, null, "Approve the Android screen-capture prompt on the device, or use --capture-backend auto/screenrecord.");
                    throw new MediaProjectionConsentException(message);
                }

                Report(reportPhase, "mediaprojection_consent", ViewStartupPhaseStatus.Succeeded, "Android MediaProjection consent was approved.");
            }
            else
            {
                var shellCommand = string.Join(
                    " ", $"CLASSPATH={ShellQuote(package.RemotePath)}", "app_process", "/", ShellQuote(package.MainClass), "--socket", ShellQuote(socketName), "--codec", ShellQuote(request.Codec), "--max-size", request.MaxSize.ToString(System.Globalization.CultureInfo.InvariantCulture), "--max-fps", request.MaxFps.ToString(System.Globalization.CultureInfo.InvariantCulture), "--video-bit-rate", ShellQuote(request.VideoBitRate));
                Report(reportPhase, "screenrecord_process", ViewStartupPhaseStatus.Started, "Starting screenrecord helper process.", package.MainClass);
                _screenrecordShell = await adbClient.StartShellAsync(shellCommand, cancellationToken).ConfigureAwait(false);
                Report(reportPhase, "screenrecord_process", ViewStartupPhaseStatus.Succeeded, "Screenrecord helper process started.");
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

    private static void Report(Action<ViewStartupPhase>? reportPhase, string phase, string status, string summary, string? detail = null, string? recommendation = null) =>
        reportPhase?.Invoke(new ViewStartupPhase(phase, status, summary, string.IsNullOrWhiteSpace(detail) ? null : detail, recommendation));

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

    private static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private static async Task<bool> TryApproveMediaProjectionConsentAsync(IAdbClient adbClient, CancellationToken cancellationToken)
    {
        const int maxAttempts = 8;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var uiXml = await DumpUiHierarchyAsync(adbClient, cancellationToken).ConfigureAwait(false);
            if (uiXml is null)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (TryFindStartNowButtonCenter(uiXml, out var x, out var y))
            {
                var tap = await adbClient.ShellAsync($"input tap {x} {y}", cancellationToken).ConfigureAwait(false);
                tap.EnsureSuccess("view helper MediaProjection consent tap failed");
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    private static async Task<string?> DumpUiHierarchyAsync(IAdbClient adbClient, CancellationToken cancellationToken)
    {
        const string remotePath = "/data/local/tmp/luotsi-view-window.xml";
        using var dumpCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        dumpCancellation.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            var dump = await adbClient.ShellAsync($"uiautomator dump {remotePath} >/dev/null && cat {remotePath} && rm -f {remotePath}", dumpCancellation.Token).ConfigureAwait(false);
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
            // ignored
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
