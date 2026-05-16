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
public sealed record ViewDoctorCheck(string Name, bool Ok, string Summary, string? Detail = null);

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
        }
        else
        {
            checks.Add(new ViewDoctorCheck(
                "preflight",
                false,
                "Skipped device preflight because the configured device is not currently visible to adb."));
        }

        checks.Add(CheckRecorder(options));

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
                return new ViewDoctorCheck("decoder", false, "FFmpeg native decoder is not ready.", ex.Message);
            }
        }

        if (string.Equals(options.Decoder, "wmf", StringComparison.OrdinalIgnoreCase))
        {
            return new ViewDoctorCheck("decoder", false, "WMF view decoder is not implemented yet.");
        }

        return new ViewDoctorCheck("decoder", false, $"Unsupported view decoder '{options.Decoder}'.");
    }

    private ViewDoctorCheck CheckHelperPackage()
    {
        try
        {
            var package = _helperPackageLocator.Resolve();
            return new ViewDoctorCheck("helper_package", true, $"Android view helper is ready ({package.Version}).", package.LocalPath);
        }
        catch (Exception ex)
        {
            return new ViewDoctorCheck("helper_package", false, "Android view helper package is not ready.", ex.Message);
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
                    new ViewDoctorCheck("device_visibility", false, $"Configured device '{options.DeviceSelector}' is not visible to adb.", detail),
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
                new ViewDoctorCheck("device_visibility", false, "Unable to enumerate adb-visible devices.", ex.Message),
                Array.Empty<DeviceInfo>());
        }
    }

    private async Task<(ViewDoctorCheck Check, PreflightResult? Preflight)> CheckPreflightAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _deviceHost.PreflightAsync(null).ConfigureAwait(false);
            var summary = $"Device preflight passed for {result.Model} (Android {result.AndroidRelease}, SDK {result.Sdk}).";
            return (new ViewDoctorCheck("preflight", true, summary, result.CurrentFocus), result);
        }
        catch (Exception ex)
        {
            return (new ViewDoctorCheck("preflight", false, "Device preflight failed.", ex.Message), null);
        }
    }

    private ViewDoctorCheck CheckRecorder(ViewOptions options)
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
                recorder.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }

            return new ViewDoctorCheck("recording", true, "Live recording target is ready.", options.RecordPath);
        }
        catch (Exception ex)
        {
            return new ViewDoctorCheck("recording", false, "Live recording target is not ready.", ex.Message);
        }
    }
}