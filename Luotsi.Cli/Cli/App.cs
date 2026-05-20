using Luotsi.Cli.Cli.Composition;
using Luotsi.Cli.Cli.Envelope;
using Luotsi.Cli.Cli.Inspect;
using Luotsi.Cli.Cli.Routing;

namespace Luotsi.Cli.Cli;

/// <summary>
/// Entry point for the Luotsi command-line application.
/// </summary>
public sealed class App
{
    private readonly AppExecutionShell _executionShell;
    private readonly AppCommandFamilyRouter _commandFamilyRouter;

    /// <summary>
    /// Creates the CLI application with default services.
    /// </summary>
    public App()
        : this(null)
    {
    }

    /// <summary>
    /// Creates the CLI application with optional service overrides.
    /// </summary>
    /// <param name="dependencies">Optional dependency overrides for tests or specialized hosting.</param>
    public App(AppDependencies? dependencies)
    {
        dependencies ??= new AppDependencies();

        var infrastructure = AppInfrastructureCompositionBuilder.Build(dependencies);
        var hostedCommands = AppHostedCommandCompositionBuilder.Build(new(
            infrastructure.TimeProvider,
            infrastructure.Console,
            infrastructure.FileSystem,
            infrastructure.Environment,
            infrastructure.Delay,
            infrastructure.ProfileCoordinator));
        var viewCommands = AppViewCommandCompositionBuilder.Build(new(
            dependencies,
            infrastructure.TimeProvider,
            infrastructure.Console,
            infrastructure.Environment,
            infrastructure.FileSystem,
            infrastructure.ProcessRunner,
            infrastructure.AdbClientFactory,
            infrastructure.IdGenerator,
            hostedCommands.EnvelopeWriter,
            infrastructure.ProfileCoordinator,
            infrastructure.DeviceHostLauncher));
        _executionShell = new AppExecutionShell(new AppExecutionShellDependencies
        {
            Console = infrastructure.Console,
            TimeProvider = infrastructure.TimeProvider,
            FailureResponder = new AppCommandFailureResponder(hostedCommands.EnvelopeWriter)
        });
        _commandFamilyRouter = new AppCommandFamilyRouter(new AppCommandFamilyRouterDependencies
        {
            RouteBootstrapper = new AppCommandRouteBootstrapper(new AppCommandRouteBootstrapperDependencies
            {
                TimeProvider = infrastructure.TimeProvider,
                FileSystem = infrastructure.FileSystem,
                Environment = infrastructure.Environment,
                ProfileCoordinator = infrastructure.ProfileCoordinator,
                DeviceHostLauncher = infrastructure.DeviceHostLauncher
            }),
            CommandHost = hostedCommands.CommandHost,
            ViewSessionCommandPreparer = viewCommands.ViewSessionCommandPreparer,
            InspectSessionLauncher = new InspectSessionLauncher(infrastructure.DeviceHostLauncher, infrastructure.Console, infrastructure.TimeProvider),
            ViewDiagnosticsLauncher = viewCommands.ViewDiagnosticsLauncher
        });
    }

    /// <summary>
    /// Runs the command-line application.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>The process exit code.</returns>
    public Task<int> RunAsync(string[] args) => _executionShell.RunAsync(args, _commandFamilyRouter.DispatchAsync);
}
