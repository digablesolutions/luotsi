using Luotsi.Cli.Cli.Envelope;
using Luotsi.Cli.Cli.Doctor;
using Luotsi.Cli.Cli.Hosting;
using Luotsi.Cli.Cli.View;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.View.Diagnostics;
using Luotsi.Cli.View.Session;

namespace Luotsi.Cli.Cli.Composition;

internal static class AppViewCommandCompositionBuilder
{
    public static AppViewCommandComposition Build(AppViewCommandCompositionBuilderDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);

        var overrides = dependencies.Overrides;
        var resolvedViewSessionFactory = overrides.ViewSessionFactory ?? new DefaultViewSessionFactory(
            dependencies.Console,
            dependencies.TimeProvider,
            dependencies.AdbClientFactory,
            dependencies.ProcessRunner,
            dependencies.Environment,
            dependencies.FileSystem,
            dependencies.IdGenerator);
        var resolvedViewDoctorFactory = overrides.ViewDoctorFactory ?? new DefaultViewDoctorFactory(
            dependencies.Environment,
            dependencies.FileSystem,
            dependencies.ProcessRunner);
        var resolvedViewSetupFactory = overrides.ViewSetupFactory ?? new DefaultViewSetupFactory(
            dependencies.Environment,
            dependencies.FileSystem,
            dependencies.ProcessRunner,
            dependencies.AdbClientFactory,
            resolvedViewDoctorFactory);
        var viewDiagnosticCommandHost = new ViewDiagnosticCommandHost(new(
            dependencies.Environment,
            dependencies.EnvelopeWriter,
            resolvedViewDoctorFactory,
            resolvedViewSetupFactory));
        var doctorCommandHost = new DoctorCommandHost(new(
            dependencies.Environment,
            dependencies.EnvelopeWriter,
            resolvedViewDoctorFactory,
            resolvedViewSetupFactory,
            new FfmpegSetupProvisioner(
                dependencies.Environment,
                dependencies.FileSystem,
                dependencies.ProcessRunner)));

        return new(
            new ViewSessionCommandPreparer(
                dependencies.DeviceHostLauncher,
                resolvedViewSessionFactory,
                dependencies.ProfileCoordinator,
                dependencies.Environment),
            new ViewDiagnosticsLauncher(dependencies.DeviceHostLauncher, viewDiagnosticCommandHost),
            new DoctorCommandLauncher(dependencies.DeviceHostLauncher, doctorCommandHost));
    }
}

internal sealed record AppViewCommandCompositionBuilderDependencies(
    AppDependencies Overrides,
    TimeProvider TimeProvider,
    IConsoleIo Console,
    IEnvironmentVariables Environment,
    IFileSystem FileSystem,
    IProcessRunner ProcessRunner,
    IAdbClientFactory AdbClientFactory,
    IUniqueIdGenerator IdGenerator,
    AppCommandEnvelopeWriter EnvelopeWriter,
    ViewProfileCoordinator ProfileCoordinator,
    DeviceHostLauncher DeviceHostLauncher);

internal sealed record AppViewCommandComposition(
    ViewSessionCommandPreparer ViewSessionCommandPreparer,
    ViewDiagnosticsLauncher ViewDiagnosticsLauncher,
    DoctorCommandLauncher DoctorCommandLauncher);