using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.View.Contracts;

namespace Luotsi.Cli.Hosts.Android.View;

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
        reportPhase?.Invoke(new ViewStartupPhase(phase, status, summary, string.IsNullOrWhiteSpace(detail) ? null : detail, recommendation));

    internal static string PackageDetail(AndroidViewHelperPackage package) =>
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
