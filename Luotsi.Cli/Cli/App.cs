using Luotsi.Cli.Infrastructure.Devices;
using Luotsi.Cli.Infrastructure.Ids;
using Luotsi.Cli.Infrastructure.Processes;
using Luotsi.Cli.Infrastructure.System;
using Luotsi.Cli.Infrastructure.Time;
using Luotsi.Cli.Scenarios;
using Luotsi.Cli.View.Diagnostics;
using Luotsi.Cli.View.Session;

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

        var resolvedTimeProvider = dependencies.TimeProvider ?? TimeProvider.System;
        var resolvedFileSystem = dependencies.FileSystem ?? new PhysicalFileSystem();
        var resolvedProcessRunner = dependencies.ProcessRunner ?? new DefaultProcessRunner();
        var resolvedDelay = dependencies.Delay ?? new TaskDelay(resolvedTimeProvider);
        var resolvedConsole = dependencies.Console ?? new SystemConsoleIo();
        var resolvedEnvironment = dependencies.Environment ?? new SystemEnvironmentVariables();
        var resolvedIdGenerator = dependencies.IdGenerator ?? new GuidUniqueIdGenerator();
        var resolvedAdbClientFactory = dependencies.AdbClientFactory ?? new DefaultAdbClientFactory();
        var resolvedDeviceHostFactory = dependencies.DeviceHostFactory ?? new DefaultDeviceHostFactory(
            resolvedAdbClientFactory,
            resolvedProcessRunner,
            resolvedDelay,
            resolvedFileSystem,
            resolvedTimeProvider,
            resolvedEnvironment,
            resolvedIdGenerator);
        var resolvedViewSessionFactory = dependencies.ViewSessionFactory ?? new DefaultViewSessionFactory(
            resolvedConsole,
            resolvedTimeProvider,
            resolvedAdbClientFactory,
            resolvedProcessRunner,
            resolvedEnvironment,
            resolvedFileSystem,
            resolvedIdGenerator);
        var resolvedViewDoctorFactory = dependencies.ViewDoctorFactory ?? new DefaultViewDoctorFactory(
            resolvedEnvironment,
            resolvedFileSystem,
            resolvedProcessRunner);
        var resolvedViewSetupFactory = dependencies.ViewSetupFactory ?? new DefaultViewSetupFactory(
            resolvedEnvironment,
            resolvedFileSystem,
            resolvedProcessRunner,
            resolvedAdbClientFactory,
            resolvedViewDoctorFactory);
        var resolvedViewProfileStore = dependencies.ViewProfileStore ?? new JsonViewProfileStore(resolvedFileSystem, resolvedEnvironment);
        var profileCoordinator = new ViewProfileCoordinator(resolvedViewProfileStore);
        var scenarioTemplateResolver = new ScenarioTemplateResolver(resolvedTimeProvider, resolvedEnvironment);
        var scenarioCatalog = new ScenarioCatalog(resolvedFileSystem, scenarioTemplateResolver);
        var scenarioRunPlanner = new ScenarioRunPlanner(scenarioCatalog);
        var scenarioExecutorFactory = new ScenarioExecutorFactory(resolvedFileSystem, resolvedTimeProvider, resolvedDelay, scenarioTemplateResolver);
        var scenarioBatchExecutorFactory = new ScenarioBatchExecutorFactory(scenarioExecutorFactory);
        var scenarioRunEventCoordinatorFactory = new ScenarioRunEventCoordinatorFactory(resolvedFileSystem, resolvedTimeProvider);
        var envelopeWriter = new AppCommandEnvelopeWriter(resolvedConsole, resolvedTimeProvider);
        var commandDispatcher = new AppCommandDispatcher(
            new AdbSubcommandDispatcher(),
            new ScenarioCommandDispatcher(scenarioRunPlanner, scenarioExecutorFactory, scenarioBatchExecutorFactory, scenarioRunEventCoordinatorFactory),
            profileCoordinator);
        var commandHost = new AppCommandHost(new AppCommandHostDependencies
        {
            Environment = resolvedEnvironment,
            EnvelopeWriter = envelopeWriter,
            ExitCodeResolver = new AppCommandExitCodeResolver(),
            ProfileCoordinator = profileCoordinator,
            CommandDispatcher = commandDispatcher,
            ViewDoctorFactory = resolvedViewDoctorFactory,
            ViewSetupFactory = resolvedViewSetupFactory
        });
        var deviceHostLauncher = new DeviceHostLauncher(resolvedDeviceHostFactory, resolvedEnvironment);
        _executionShell = new AppExecutionShell(new AppExecutionShellDependencies
        {
            Console = resolvedConsole,
            TimeProvider = resolvedTimeProvider,
            FailureResponder = new AppCommandFailureResponder(envelopeWriter)
        });
        _commandFamilyRouter = new AppCommandFamilyRouter(new AppCommandFamilyRouterDependencies
        {
            TimeProvider = resolvedTimeProvider,
            FileSystem = resolvedFileSystem,
            Environment = resolvedEnvironment,
            ProfileCoordinator = profileCoordinator,
            CommandHost = commandHost,
            ViewSessionCommandPreparer = new ViewSessionCommandPreparer(deviceHostLauncher, resolvedViewSessionFactory, profileCoordinator, resolvedEnvironment),
            InspectSessionLauncher = new InspectSessionLauncher(deviceHostLauncher, resolvedConsole, resolvedTimeProvider),
            ViewDoctorLauncher = new ViewDoctorLauncher(deviceHostLauncher, commandHost),
            DeviceHostLauncher = deviceHostLauncher
        });
    }

    /// <summary>
    /// Runs the command-line application.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>The process exit code.</returns>
    public Task<int> RunAsync(string[] args) => _executionShell.RunAsync(args, _commandFamilyRouter.DispatchAsync);
}
