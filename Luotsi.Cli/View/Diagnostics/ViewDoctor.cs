using Luotsi.Cli.Hosts.Android.View;
using Luotsi.Cli.Infrastructure;
using Luotsi.Cli.Models;
using Luotsi.Cli.View.Backends.Ffmpeg;

namespace Luotsi.Cli.View;

/// <summary>
/// Produces connection diagnostics for the built-in view session.
/// </summary>
public interface IViewDoctor
{
    /// <summary>
    /// Diagnoses whether the current host/device setup is ready for live view.
    /// </summary>
    /// <param name="options">Applied view options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Diagnostic report.</returns>
    Task<ViewDoctorResult> DiagnoseAsync(ViewOptions options, CancellationToken cancellationToken = default);
}

/// <summary>
/// Creates connection diagnostics for a concrete device host.
/// </summary>
public interface IViewDoctorFactory
{
    /// <summary>
    /// Creates a doctor bound to a concrete device host.
    /// </summary>
    /// <param name="deviceHost">Device host to probe.</param>
    /// <returns>Diagnostic helper.</returns>
    IViewDoctor Create(IDeviceHost deviceHost);
}

/// <summary>
/// Individual doctor check result.
/// </summary>
/// <param name="Name">Check name.</param>
/// <param name="Ok">Whether the check passed.</param>
/// <param name="Summary">Human-readable summary.</param>
/// <param name="Detail">Optional detail payload.</param>
/// <param name="Recommendation">Concrete fallback or fix to try when the check fails.</param>
public sealed record ViewDoctorCheck(string Name, bool Ok, string Summary, string? Detail = null, string? Recommendation = null);

/// <summary>
/// Aggregated doctor result for a requested view configuration.
/// </summary>
/// <param name="Ready">Whether the configuration is ready for live view.</param>
/// <param name="Preset">Applied preset name.</param>
/// <param name="AppliedOptions">Resolved view options after presets/defaults.</param>
/// <param name="ConnectedDevices">Current adb-visible devices.</param>
/// <param name="Preflight">Optional preflight snapshot for the configured device.</param>
/// <param name="Checks">Executed checks.</param>
public sealed record ViewDoctorResult(
    bool Ready,
    string Preset,
    ViewOptions AppliedOptions,
    IReadOnlyList<DeviceInfo> ConnectedDevices,
    PreflightResult? Preflight,
    IReadOnlyList<ViewDoctorCheck> Checks);

/// <summary>
/// Default doctor factory for the built-in view subsystem.
/// </summary>
public sealed class DefaultViewDoctorFactory(
    IEnvironmentVariables environment,
    IFileSystem fileSystem,
    IProcessRunner processRunner,
    ILibavNativeLibraryBinder? libavBinder = null) : IViewDoctorFactory
{
    private readonly IEnvironmentVariables _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly IProcessRunner _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    private readonly ILibavNativeLibraryBinder? _libavBinder = libavBinder;

    /// <inheritdoc />
    public IViewDoctor Create(IDeviceHost deviceHost) => new ViewDoctor(
        deviceHost,
        new AndroidViewHelperPackageLocator(_environment, _fileSystem),
        new DefaultViewRecorderFactory(_fileSystem, _processRunner, _environment),
        _environment,
        _libavBinder);
}

/// <summary>
/// Executes concrete host, device, and configuration checks for live view.
/// </summary>
public sealed class ViewDoctor(
    IDeviceHost deviceHost,
    IAndroidViewHelperPackageLocator helperPackageLocator,
    IViewRecorderFactory recorderFactory,
    IEnvironmentVariables environment,
    ILibavNativeLibraryBinder? libavBinder = null) : IViewDoctor
{
    private readonly IDeviceHost _deviceHost = deviceHost ?? throw new ArgumentNullException(nameof(deviceHost));
    private readonly IAdbCommandHost? _adbCommandHost = deviceHost as IAdbCommandHost;
    private readonly IAndroidViewHelperPackageLocator _helperPackageLocator = helperPackageLocator ?? throw new ArgumentNullException(nameof(helperPackageLocator));
    private readonly IViewRecorderFactory _recorderFactory = recorderFactory ?? throw new ArgumentNullException(nameof(recorderFactory));
    private readonly IEnvironmentVariables _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    private readonly ILibavNativeLibraryBinder? _libavBinder = libavBinder;

    /// <inheritdoc />
    public async Task<ViewDoctorResult> DiagnoseAsync(ViewOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var checks = new List<ViewDoctorCheck>
        {
            CheckDecoder(options),
            CheckCaptureBackend(options),
            CheckHelperPackage()
        };

        IReadOnlyList<DeviceInfo> connectedDevices = Array.Empty<DeviceInfo>();
        PreflightResult? preflight = null;
        var deviceCheck = await CheckDeviceVisibilityAsync(options, cancellationToken).ConfigureAwait(false);
        connectedDevices = deviceCheck.Devices;
        checks.Add(deviceCheck.Check);

        if (deviceCheck.Check.Ok)
        {
            var preflightCheck = await CheckPreflightAsync(cancellationToken).ConfigureAwait(false);
            preflight = preflightCheck.Preflight;
            checks.Add(preflightCheck.Check);
            checks.AddRange(CheckMediaProjectionReadiness(options, preflight));
        }
        else
        {
            checks.Add(new ViewDoctorCheck(
                "preflight",
                false,
                "Skipped device preflight because the configured device is not currently visible to adb."));
            checks.AddRange(CheckMediaProjectionReadiness(options, null));
        }

        checks.Add(await CheckRecorderAsync(options).ConfigureAwait(false));

        return new ViewDoctorResult(
            checks.All(static check => check.Ok),
            options.PresetName,
            options,
            connectedDevices,
            preflight,
            checks);
    }

    private ViewDoctorCheck CheckDecoder(ViewOptions options)
    {
        if (string.Equals(options.Decoder, "ffmpeg", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var rootPath = new LibavNativeLibraryLoader(_environment, _libavBinder).EnsureLoaded();
                var detail = string.IsNullOrWhiteSpace(rootPath) ? "process-path" : rootPath;
                return new ViewDoctorCheck("decoder", true, "FFmpeg native decoder is ready.", detail);
            }
            catch (Exception ex)
            {
                return new ViewDoctorCheck("decoder", false, "FFmpeg native decoder is not ready.", ex.Message, "Set LUOTSI_FFMPEG_ROOT or run view with --defaults --decoder ffmpeg after installing the bundled FFmpeg libraries.");
            }
        }

        if (string.Equals(options.Decoder, "wmf", StringComparison.OrdinalIgnoreCase))
        {
            return new ViewDoctorCheck("decoder", false, "WMF view decoder is not implemented yet.", null, "Use --decoder ffmpeg.");
        }

        return new ViewDoctorCheck("decoder", false, $"Unsupported view decoder '{options.Decoder}'.", null, "Use --decoder ffmpeg.");
    }

    private static ViewDoctorCheck CheckCaptureBackend(ViewOptions options)
    {
        return options.CaptureBackend.ToLowerInvariant() switch
        {
            ViewCaptureBackends.Auto => new ViewDoctorCheck(
                "capture_backend",
                true,
                "Capture backend is auto; the host will prefer MediaProjection and keep screenrecord available as the explicit fallback.",
                "preferred=mediaprojection; fallback=screenrecord"),
            ViewCaptureBackends.Screenrecord => new ViewDoctorCheck(
                "capture_backend",
                true,
                "Capture backend is screenrecord.",
                "screenrecord has Android's 180-second session limit."),
            ViewCaptureBackends.MediaProjection => new ViewDoctorCheck(
                "capture_backend",
                true,
                "Capture backend is mediaprojection.",
                "Requires Android screen-capture consent and an AVC MediaCodec encoder."),
            _ => new ViewDoctorCheck(
                "capture_backend",
                false,
                $"Unsupported capture backend '{options.CaptureBackend}'.",
                null,
                "Use --capture-backend auto, screenrecord, or mediaprojection.")
        };
    }

    private static IReadOnlyList<ViewDoctorCheck> CheckMediaProjectionReadiness(ViewOptions options, PreflightResult? preflight)
    {
        if (!UsesMediaProjection(options))
        {
            return [];
        }

        var checks = new List<ViewDoctorCheck>();
        if (preflight is null)
        {
            checks.Add(new ViewDoctorCheck(
                "mediaprojection_api",
                false,
                "Unable to verify MediaProjection API support because device preflight did not run.",
                null,
                "Fix device visibility/preflight first, or use --capture-backend screenrecord."));
        }
        else if (int.TryParse(preflight.Sdk, out var sdk) && sdk >= 21)
        {
            checks.Add(new ViewDoctorCheck(
                "mediaprojection_api",
                true,
                $"Device SDK {sdk} supports MediaProjection.",
                $"Android {preflight.AndroidRelease}; model={preflight.Model}"));
        }
        else
        {
            checks.Add(new ViewDoctorCheck(
                "mediaprojection_api",
                false,
                $"Device SDK '{preflight.Sdk}' does not meet the MediaProjection minimum.",
                $"Android {preflight.AndroidRelease}; model={preflight.Model}",
                "Use --capture-backend screenrecord on this device."));
        }

        checks.Add(string.Equals(options.Codec, "h264", StringComparison.OrdinalIgnoreCase)
            ? new ViewDoctorCheck(
                "mediaprojection_encoder",
                true,
                "MediaProjection capture will request an AVC/H.264 MediaCodec encoder.",
                $"max_size={options.MaxSize}; max_fps={options.MaxFps}; bit_rate={options.VideoBitRate}")
            : new ViewDoctorCheck(
                "mediaprojection_encoder",
                false,
                $"MediaProjection capture currently supports only h264, not '{options.Codec}'.",
                null,
                "Use --codec h264."));

        checks.Add(CheckMediaProjectionConsent(options));

        return checks;
    }

    private static ViewDoctorCheck CheckMediaProjectionConsent(ViewOptions options)
    {
        if (string.Equals(options.CaptureBackend, ViewCaptureBackends.Auto, StringComparison.OrdinalIgnoreCase))
        {
            return new ViewDoctorCheck(
                "mediaprojection_consent",
                true,
                "MediaProjection consent will be requested by the helper activity when auto starts; screenrecord remains available if consent is denied or times out.",
                "consent_state=interactive; fallback=screenrecord");
        }

        return new ViewDoctorCheck(
            "mediaprojection_consent",
            false,
            "MediaProjection consent cannot be preflighted before the Android consent activity runs.",
            "consent_state=interactive; fallback=none",
            "Start a MediaProjection view session on the physical device and approve the Android screen-capture prompt, or use --capture-backend auto/screenrecord.");
    }

    private static bool UsesMediaProjection(ViewOptions options) =>
        string.Equals(options.CaptureBackend, ViewCaptureBackends.Auto, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(options.CaptureBackend, ViewCaptureBackends.MediaProjection, StringComparison.OrdinalIgnoreCase);

    private ViewDoctorCheck CheckHelperPackage()
    {
        try
        {
            var package = _helperPackageLocator.Resolve();
            return new ViewDoctorCheck("helper_package", true, $"Android view helper is ready ({package.Version}).", package.LocalPath);
        }
        catch (Exception ex)
        {
            return new ViewDoctorCheck("helper_package", false, "Android view helper package is not ready.", ex.Message, "Build the Android helper APK or set LUOTSI_VIEW_HELPER_APK to a valid helper package.");
        }
    }

    private async Task<(ViewDoctorCheck Check, IReadOnlyList<DeviceInfo> Devices)> CheckDeviceVisibilityAsync(ViewOptions options, CancellationToken cancellationToken)
    {
        try
        {
            var devices = await _deviceHost.GetDevicesAsync().ConfigureAwait(false);
            var matchedDevice = devices.Devices.FirstOrDefault(device =>
                string.Equals(device.Serial, options.DeviceSelector, StringComparison.OrdinalIgnoreCase));
            if (matchedDevice is null)
            {
                var detail = devices.Devices.Count == 0
                    ? "No adb-visible devices were reported."
                    : string.Join(", ", devices.Devices.Select(static device => string.IsNullOrWhiteSpace(device.Serial) ? device.Details : device.Serial));
                return (
                    new ViewDoctorCheck("device_visibility", false, $"Configured device '{options.DeviceSelector}' is not visible to adb.", detail, "Run `adb devices`, reconnect USB, authorize the device, or use `wireless` to connect a remembered TCP/IP target."),
                    devices.Devices);
            }

            if (!string.Equals(matchedDevice.Status, "device", StringComparison.OrdinalIgnoreCase))
            {
                return (
                    new ViewDoctorCheck("device_visibility", false, $"Configured device '{options.DeviceSelector}' is visible but not ready: '{matchedDevice.Status}'.", matchedDevice.Details, "If status is unauthorized, confirm the Android USB debugging prompt. If offline, reconnect the device or restart adb."),
                    devices.Devices);
            }

            var summary = string.IsNullOrWhiteSpace(matchedDevice.Status)
                ? $"Configured device '{options.DeviceSelector}' is visible to adb."
                : $"Configured device '{options.DeviceSelector}' is visible to adb with status '{matchedDevice.Status}'.";
            return (new ViewDoctorCheck("device_visibility", true, summary, matchedDevice.Details), devices.Devices);
        }
        catch (Exception ex)
        {
            return (
                new ViewDoctorCheck("device_visibility", false, "Unable to enumerate adb-visible devices.", ex.Message, "Check that adb is installed and reachable via --adb or LUOTSI_ADB."),
                Array.Empty<DeviceInfo>());
        }
    }

    private async Task<(ViewDoctorCheck Check, PreflightResult? Preflight)> CheckPreflightAsync(CancellationToken cancellationToken)
    {
        if (_adbCommandHost is null)
        {
            return (new ViewDoctorCheck("preflight", false, "Device preflight is unavailable for the current host.", null, "Use a direct adb-backed device host."), null);
        }

        try
        {
            var result = await _adbCommandHost.ReadPreflightAsync(null).ConfigureAwait(false);
            var summary = $"Device preflight passed for {result.Model} (Android {result.AndroidRelease}, SDK {result.Sdk}).";
            return (new ViewDoctorCheck("preflight", true, summary, result.CurrentFocus), result);
        }
        catch (Exception ex)
        {
            return (new ViewDoctorCheck("preflight", false, "Device preflight failed.", ex.Message, "Wake/unlock the device and verify the target app or current foreground package."), null);
        }
    }

    private async Task<ViewDoctorCheck> CheckRecorderAsync(ViewOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.RecordPath))
        {
            return new ViewDoctorCheck("recording", true, "Live recording is disabled.");
        }

        try
        {
            var recorder = _recorderFactory.Create(options);
            if (recorder is not null)
            {
                await recorder.DisposeAsync().ConfigureAwait(false);
            }

            return new ViewDoctorCheck("recording", true, "Live recording target is ready.", options.RecordPath);
        }
        catch (Exception ex)
        {
            return new ViewDoctorCheck("recording", false, "Live recording target is not ready.", ex.Message, "Use a .h264/.mp4/.mkv output path in a writable directory, or omit --record.");
        }
    }
}
