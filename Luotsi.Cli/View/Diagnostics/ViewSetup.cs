using System.Linq;
using Luotsi.Cli.Hosts.Android.View;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;
using Luotsi.Cli.View.Contracts;

namespace Luotsi.Cli.View.Diagnostics;

/// <summary>
/// Creates setup runners for concrete view hosts.
/// </summary>
public interface IViewSetupFactory
{
    IViewSetup Create(IDeviceHost deviceHost);
}

/// <summary>
/// Executes fixable setup checks for live view.
/// </summary>
public interface IViewSetup
{
    Task<ViewSetupResult> SetupAsync(ViewOptions options, bool fix, CancellationToken cancellationToken = default);
}

public sealed record ViewSetupStep(string Name, string Status, string Summary, string? Detail = null, string? Recommendation = null);

public sealed record ViewSetupResult(
    bool Ready,
    bool Fix,
    string Preset,
    ViewOptions AppliedOptions,
    IReadOnlyList<ViewSetupStep> Steps,
    ViewDoctorResult Doctor);

public sealed class DefaultViewSetupFactory(
    IEnvironmentVariables environment,
    IFileSystem fileSystem,
    IProcessRunner processRunner,
    IAdbClientFactory adbClientFactory,
    IViewDoctorFactory viewDoctorFactory) : IViewSetupFactory
{
    private readonly IEnvironmentVariables _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly IProcessRunner _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    private readonly IAdbClientFactory _adbClientFactory = adbClientFactory ?? throw new ArgumentNullException(nameof(adbClientFactory));
    private readonly IViewDoctorFactory _viewDoctorFactory = viewDoctorFactory ?? throw new ArgumentNullException(nameof(viewDoctorFactory));

    public IViewSetup Create(IDeviceHost deviceHost)
    {
        var helperPackageLocator = new AndroidViewHelperPackageLocator(_environment, _fileSystem);
        return new ViewSetup(
            deviceHost,
            helperPackageLocator,
            new ViewHostPathResolver(_environment),
            _viewDoctorFactory,
            _fileSystem,
            _processRunner,
            _adbClientFactory);
    }
}

public sealed class ViewSetup(
    IDeviceHost deviceHost,
    IAndroidViewHelperPackageLocator helperPackageLocator,
    ViewHostPathResolver pathResolver,
    IViewDoctorFactory viewDoctorFactory,
    IFileSystem fileSystem,
    IProcessRunner processRunner,
    IAdbClientFactory adbClientFactory) : IViewSetup
{
    private readonly IDeviceHost _deviceHost = deviceHost ?? throw new ArgumentNullException(nameof(deviceHost));
    private readonly IAndroidViewHelperPackageLocator _helperPackageLocator = helperPackageLocator ?? throw new ArgumentNullException(nameof(helperPackageLocator));
    private readonly AndroidViewHelperSetupProvisioner _helperProvisioner = new(
        helperPackageLocator ?? throw new ArgumentNullException(nameof(helperPackageLocator)),
        pathResolver ?? throw new ArgumentNullException(nameof(pathResolver)),
        fileSystem ?? throw new ArgumentNullException(nameof(fileSystem)),
        processRunner ?? throw new ArgumentNullException(nameof(processRunner)));
    private readonly IViewDoctorFactory _viewDoctorFactory = viewDoctorFactory ?? throw new ArgumentNullException(nameof(viewDoctorFactory));
    private readonly IProcessRunner _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    private readonly IAdbClientFactory _adbClientFactory = adbClientFactory ?? throw new ArgumentNullException(nameof(adbClientFactory));

    public async Task<ViewSetupResult> SetupAsync(ViewOptions options, bool fix, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var steps = new List<ViewSetupStep>();
        var package = await _helperProvisioner.ResolveOrBuildAsync(fix, steps.Add, cancellationToken).ConfigureAwait(false);
        if (package is not null)
        {
            await InstallAndVerifyHelperAsync(options, package, fix, steps, cancellationToken).ConfigureAwait(false);
        }

        var doctor = await _viewDoctorFactory.Create(_deviceHost).DiagnoseAsync(options, cancellationToken).ConfigureAwait(false);
        var ready = doctor.Ready && steps
            .GroupBy(static step => step.Name, StringComparer.Ordinal)
            .Select(static group => group.Last())
            .All(static step => !string.Equals(step.Status, ViewStartupPhaseStatus.Failed, StringComparison.Ordinal));
        return new ViewSetupResult(ready, fix, options.PresetName, options, steps, doctor);
    }

    private async Task InstallAndVerifyHelperAsync(ViewOptions options, AndroidViewHelperPackage package, bool fix, List<ViewSetupStep> steps, CancellationToken cancellationToken)
    {
        if (!fix)
        {
            steps.Add(new ViewSetupStep(
                "helper_install",
                ViewStartupPhaseStatus.Skipped,
                "Android view helper install was not attempted.",
                package.LocalPath,
                "Run view setup --fix to install and verify the helper on the selected device."));
            return;
        }

        var adb = _adbClientFactory.Create(options.AdbExecutable, options.DeviceSelector, _processRunner, options.CommandTimeout);
        var installer = new AndroidViewServerInstaller(
            adb,
            _helperPackageLocator,
            phase => steps.Add(new ViewSetupStep(phase.Phase, phase.Status, phase.Summary, phase.Detail, phase.Recommendation)));
        try
        {
            await installer.InstallAsync(package, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsExpectedSetupException(ex))
        {
            _ = ex;
            // The installer already reported the exact helper_install or helper_verify failure phase.
        }
    }

    private static bool IsExpectedSetupException(Exception exception) =>
        exception is InvalidOperationException or IOException or UnauthorizedAccessException;
}
