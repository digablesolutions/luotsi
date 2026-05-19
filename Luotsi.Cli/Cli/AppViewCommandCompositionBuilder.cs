using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.View.Contracts;
using Luotsi.Cli.View.Diagnostics;
using Luotsi.Cli.View.Session;

namespace Luotsi.Cli.Cli;

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
        var viewDiagnosticCommandHost = new ViewDiagnosticCommandHost(new ViewDiagnosticCommandHostDependencies
        {
            Environment = dependencies.Environment,
            EnvelopeWriter = dependencies.EnvelopeWriter,
            ViewDoctorFactory = resolvedViewDoctorFactory,
            ViewSetupFactory = resolvedViewSetupFactory
        });

        return new AppViewCommandComposition
        {
            ViewSessionCommandPreparer = new ViewSessionCommandPreparer(
                dependencies.DeviceHostLauncher,
                resolvedViewSessionFactory,
                dependencies.ProfileCoordinator,
                dependencies.Environment),
            ViewDiagnosticsLauncher = new ViewDiagnosticsLauncher(dependencies.DeviceHostLauncher, viewDiagnosticCommandHost)
        };
    }
}

internal sealed class AppViewCommandCompositionBuilderDependencies
{
    public required AppDependencies Overrides { get; init; }

    public required TimeProvider TimeProvider { get; init; }

    public required IConsoleIo Console { get; init; }

    public required IEnvironmentVariables Environment { get; init; }

    public required IFileSystem FileSystem { get; init; }

    public required IProcessRunner ProcessRunner { get; init; }

    public required IAdbClientFactory AdbClientFactory { get; init; }

    public required IUniqueIdGenerator IdGenerator { get; init; }

    public required AppCommandEnvelopeWriter EnvelopeWriter { get; init; }

    public required ViewProfileCoordinator ProfileCoordinator { get; init; }

    public required DeviceHostLauncher DeviceHostLauncher { get; init; }
}

internal sealed class AppViewCommandComposition
{
    public required ViewSessionCommandPreparer ViewSessionCommandPreparer { get; init; }

    public required ViewDiagnosticsLauncher ViewDiagnosticsLauncher { get; init; }
}