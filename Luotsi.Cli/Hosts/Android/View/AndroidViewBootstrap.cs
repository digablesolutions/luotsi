using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.View.Contracts;
using Luotsi.Cli.View.Transport;

namespace Luotsi.Cli.Hosts.Android.View;

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
            Report(reportPhase, "helper_resolve", ViewStartupPhaseStatus.Succeeded, "Resolved Android view helper package.", AndroidViewServerInstaller.PackageDetail(package));
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
            await RemoveStaleViewForwardsAsync(adbClient, $"tcp:{localPort}", reportPhase, cancellationToken).ConfigureAwait(false);

            if (string.Equals(activeBackend, ViewCaptureBackends.MediaProjection, StringComparison.Ordinal))
            {
                var consentApprover = new AndroidMediaProjectionConsentApprover(adbClient);
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
                await ReportMediaProjectionAppOpsAsync(adbClient, package.PackageName, reportPhase, cancellationToken).ConfigureAwait(false);
                Report(reportPhase, "mediaprojection_consent", ViewStartupPhaseStatus.Started, "Waiting for Android MediaProjection consent prompt.", "uiautomator=start-now");
                var approved = await consentApprover.TryApproveAsync(cancellationToken).ConfigureAwait(false);
                if (!approved)
                {
                    const string message = "MediaProjection consent prompt was not approved or could not be detected.";
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

    private static async Task RemoveStaleViewForwardsAsync(
        IAdbClient adbClient,
        string currentLocal,
        Action<ViewStartupPhase>? reportPhase,
        CancellationToken cancellationToken)
    {
        Report(reportPhase, "adb_forward_cleanup", ViewStartupPhaseStatus.Started, "Checking for stale Luotsi adb forwards.");
        var list = await adbClient.RunAsync(["forward", "--list"], cancellationToken).ConfigureAwait(false);
        if (list.ExitCode != 0)
        {
            Report(reportPhase, "adb_forward_cleanup", ViewStartupPhaseStatus.Skipped, "Could not list adb forwards before view startup.", list.Stderr.Trim(), "Run `adb forward --list` or restart the adb server if view transport fails.");
            return;
        }

        var otherLuotsiLocals = ParseStaleViewForwardLocals(list.Stdout)
            .Where(local => !string.Equals(local, currentLocal, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Report(
            reportPhase,
            "adb_forward_cleanup",
            otherLuotsiLocals.Length == 0 ? ViewStartupPhaseStatus.Succeeded : ViewStartupPhaseStatus.Skipped,
            otherLuotsiLocals.Length == 0
                ? "No other Luotsi adb forwards were found."
                : "Other Luotsi adb forwards were found and left in place.",
            otherLuotsiLocals.Length == 0 ? null : string.Join(", ", otherLuotsiLocals),
            otherLuotsiLocals.Length == 0 ? null : "Use `luotsi lab doctor --fix` or restart adb when you know those forwards are stale.");
    }

    private static IReadOnlyList<string> ParseStaleViewForwardLocals(string stdout)
    {
        var locals = new List<string>();
        foreach (var rawLine in stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var tokens = rawLine.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tokens.Length < 3 || !tokens.Any(static token => token.StartsWith($"localabstract:{AndroidRuntimeDefaults.ViewSocketPrefix}", StringComparison.Ordinal)))
            {
                continue;
            }

            var localSpec = tokens.FirstOrDefault(static token => token.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(localSpec))
            {
                locals.Add(localSpec);
            }
        }

        return locals;
    }

    private static async Task ReportMediaProjectionAppOpsAsync(
        IAdbClient adbClient,
        string packageName,
        Action<ViewStartupPhase>? reportPhase,
        CancellationToken cancellationToken)
    {
        Report(reportPhase, "mediaprojection_appops", ViewStartupPhaseStatus.Started, "Checking MediaProjection app-op state.", packageName);
        var appOps = await adbClient.RunAsync(["shell", "cmd", "appops", "get", packageName, "PROJECT_MEDIA"], cancellationToken).ConfigureAwait(false);
        if (appOps.ExitCode != 0)
        {
            Report(reportPhase, "mediaprojection_appops", ViewStartupPhaseStatus.Skipped, "MediaProjection app-op state is not available on this device.", appOps.Stderr.Trim());
            return;
        }

        var output = appOps.Stdout.Trim();
        if (output.Contains("deny", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("ignore", StringComparison.OrdinalIgnoreCase))
        {
            Report(
                reportPhase,
                "mediaprojection_appops",
                ViewStartupPhaseStatus.Failed,
                "MediaProjection app-op is currently denied for the helper package.",
                output,
                $"Run `adb shell cmd appops set {packageName} PROJECT_MEDIA allow`, approve the Android consent prompt, or use --capture-backend screenrecord.");
            return;
        }

        Report(reportPhase, "mediaprojection_appops", ViewStartupPhaseStatus.Succeeded, "MediaProjection app-op is not denied.", string.IsNullOrWhiteSpace(output) ? "no explicit app-op entry" : output);
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
            // ignored
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
