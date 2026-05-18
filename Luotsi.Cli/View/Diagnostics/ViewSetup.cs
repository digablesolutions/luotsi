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

    public IViewSetup Create(IDeviceHost deviceHost) => new ViewSetup(
        deviceHost,
        new AndroidViewHelperPackageLocator(_environment, _fileSystem),
        new ViewHostPathResolver(_environment),
        _viewDoctorFactory,
        _fileSystem,
        _processRunner,
        _adbClientFactory);
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
    private const string HelperProjectDirectory = "Luotsi.ViewServer.Android";
    private readonly IDeviceHost _deviceHost = deviceHost ?? throw new ArgumentNullException(nameof(deviceHost));
    private readonly IAndroidViewHelperPackageLocator _helperPackageLocator = helperPackageLocator ?? throw new ArgumentNullException(nameof(helperPackageLocator));
    private readonly ViewHostPathResolver _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
    private readonly IViewDoctorFactory _viewDoctorFactory = viewDoctorFactory ?? throw new ArgumentNullException(nameof(viewDoctorFactory));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly IProcessRunner _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    private readonly IAdbClientFactory _adbClientFactory = adbClientFactory ?? throw new ArgumentNullException(nameof(adbClientFactory));

    public async Task<ViewSetupResult> SetupAsync(ViewOptions options, bool fix, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var steps = new List<ViewSetupStep>();
        var package = await ResolveOrBuildHelperAsync(fix, steps, cancellationToken).ConfigureAwait(false);
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

    private async Task<AndroidViewHelperPackage?> ResolveOrBuildHelperAsync(bool fix, List<ViewSetupStep> steps, CancellationToken cancellationToken)
    {
        if (TryResolveHelper(steps, out var package))
        {
            return package;
        }

        if (!fix)
        {
            steps.Add(new ViewSetupStep(
                "helper_build",
                ViewStartupPhaseStatus.Skipped,
                "Android view helper build was not attempted.",
                null,
                "Run view setup --fix or build Luotsi.ViewServer.Android with Gradle."));
            return null;
        }

        var projectDirectory = ResolveHelperProjectDirectory();
        if (projectDirectory is null)
        {
            steps.Add(new ViewSetupStep(
                "helper_build",
                ViewStartupPhaseStatus.Failed,
                "Android view helper project was not found.",
                HelperProjectDirectory,
                "Run this command from the repository root or set LUOTSI_VIEW_HELPER_APK to a built APK."));
            return null;
        }

        var wrapper = ResolveGradleWrapper(projectDirectory);
        if (wrapper is null)
        {
            steps.Add(new ViewSetupStep(
                "helper_build",
                ViewStartupPhaseStatus.Failed,
                "Gradle wrapper was not found for the Android view helper.",
                projectDirectory,
                "Build the helper manually and set LUOTSI_VIEW_HELPER_APK."));
            return null;
        }

        steps.Add(new ViewSetupStep("helper_build", ViewStartupPhaseStatus.Started, "Building Android view helper APK.", projectDirectory));
        var build = await _processRunner.RunAsync(wrapper, ["-p", projectDirectory, ":app:assembleDebug"], cancellationToken).ConfigureAwait(false);
        if (build.ExitCode != 0)
        {
            steps.Add(new ViewSetupStep("helper_build", ViewStartupPhaseStatus.Failed, "Android view helper build failed.", PreferError(build), "Fix the Gradle build, then rerun view setup --fix."));
            return null;
        }

        steps.Add(new ViewSetupStep("helper_build", ViewStartupPhaseStatus.Succeeded, "Android view helper APK built.", build.Stdout.Trim()));
        return TryResolveHelper(steps, out package) ? package : null;
    }

    private bool TryResolveHelper(List<ViewSetupStep> steps, out AndroidViewHelperPackage? package)
    {
        try
        {
            package = _helperPackageLocator.Resolve();
            steps.Add(new ViewSetupStep(
                "helper_resolve",
                ViewStartupPhaseStatus.Succeeded,
                "Android view helper package resolved.",
                $"path={package.LocalPath}; source={package.ResolutionSource}; size={package.LocalSizeBytes?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"}; sha256={package.LocalSha256 ?? "unknown"}"));
            return true;
        }
        catch (Exception ex) when (IsExpectedSetupException(ex))
        {
            package = null;
            steps.Add(new ViewSetupStep(
                "helper_resolve",
                ViewStartupPhaseStatus.Failed,
                "Android view helper package is not ready.",
                ex.Message,
                "Build the helper APK with view setup --fix or set LUOTSI_VIEW_HELPER_APK."));
            return false;
        }
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

    private string? ResolveHelperProjectDirectory()
    {
        var candidates = _pathResolver.GetRepositoryRelativeDirectoryCandidates(HelperProjectDirectory);
        return candidates.Where(_fileSystem.DirectoryExists).FirstOrDefault();
    }

    private string? ResolveGradleWrapper(string projectDirectory)
    {
        string[] candidates = OperatingSystem.IsWindows()
            ? [Path.Join(projectDirectory, "gradlew.bat"), Path.Join(projectDirectory, "gradlew")]
            : [Path.Join(projectDirectory, "gradlew"), Path.Join(projectDirectory, "gradlew.bat")];
        return candidates.FirstOrDefault(_fileSystem.FileExists);
    }

    private static string PreferError(ProcessResult result) =>
        string.IsNullOrWhiteSpace(result.Stderr) ? result.Stdout.Trim() : result.Stderr.Trim();

    private static bool IsExpectedSetupException(Exception exception) =>
        exception is InvalidOperationException or IOException or UnauthorizedAccessException;
}
