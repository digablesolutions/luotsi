using Luotsi.Cli.Hosts.Android.View;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;
using Luotsi.Cli.View.Contracts;

namespace Luotsi.Cli.View.Diagnostics;

internal sealed class AndroidViewHelperSetupProvisioner(
    IAndroidViewHelperPackageLocator helperPackageLocator,
    ViewHostPathResolver pathResolver,
    IFileSystem fileSystem,
    IProcessRunner processRunner)
{
    private const string HelperProjectDirectory = "Luotsi.ViewServer.Android";

    private readonly IAndroidViewHelperPackageLocator _helperPackageLocator = helperPackageLocator ?? throw new ArgumentNullException(nameof(helperPackageLocator));
    private readonly ViewHostPathResolver _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly IProcessRunner _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));

    public async Task<AndroidViewHelperPackage?> ResolveOrBuildAsync(bool fix, Action<ViewSetupStep> reportStep, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reportStep);

        if (TryResolve(reportStep, out var package))
        {
            return package;
        }

        if (!fix)
        {
            reportStep(new ViewSetupStep(
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
            reportStep(new ViewSetupStep(
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
            reportStep(new ViewSetupStep(
                "helper_build",
                ViewStartupPhaseStatus.Failed,
                "Gradle wrapper was not found for the Android view helper.",
                projectDirectory,
                "Build the helper manually and set LUOTSI_VIEW_HELPER_APK."));
            return null;
        }

        reportStep(new ViewSetupStep("helper_build", ViewStartupPhaseStatus.Started, "Building Android view helper APK.", projectDirectory));
        var build = await _processRunner.RunAsync(wrapper, ["-p", projectDirectory, ":app:assembleRelease"], cancellationToken).ConfigureAwait(false);
        if (build.ExitCode != 0)
        {
            reportStep(new ViewSetupStep("helper_build", ViewStartupPhaseStatus.Failed, "Android view helper build failed.", PreferError(build), "Fix the Gradle build, then rerun view setup --fix."));
            return null;
        }

        reportStep(new ViewSetupStep("helper_build", ViewStartupPhaseStatus.Succeeded, "Android view helper APK built.", build.Stdout.Trim()));
        return TryResolve(reportStep, out package) ? package : null;
    }

    private bool TryResolve(Action<ViewSetupStep> reportStep, out AndroidViewHelperPackage? package)
    {
        try
        {
            package = _helperPackageLocator.Resolve();
            reportStep(new ViewSetupStep(
                "helper_resolve",
                ViewStartupPhaseStatus.Succeeded,
                "Android view helper package resolved.",
                $"path={package.LocalPath}; source={package.ResolutionSource}; size={package.LocalSizeBytes?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"}; sha256={package.LocalSha256 ?? "unknown"}"));
            return true;
        }
        catch (Exception ex) when (IsExpectedSetupException(ex))
        {
            package = null;
            reportStep(new ViewSetupStep(
                "helper_resolve",
                ViewStartupPhaseStatus.Failed,
                "Android view helper package is not ready.",
                ex.Message,
                "Build the helper APK with view setup --fix or set LUOTSI_VIEW_HELPER_APK."));
            return false;
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
