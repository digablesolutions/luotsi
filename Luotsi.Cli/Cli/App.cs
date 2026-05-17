using Luotsi.Cli.Infrastructure;
using Luotsi.Cli.View;

namespace Luotsi.Cli.Cli;

/// <summary>
/// Entry point for the Luotsi command-line application.
/// </summary>
public sealed class App
{
    private readonly AppExecutionShell _executionShell;
    private readonly AppCommandFamilyRouter _commandFamilyRouter;

    public App(
        TimeProvider? timeProvider = null,
        IFileSystem? fileSystem = null,
        IProcessRunner? processRunner = null,
        IDelay? delay = null,
        IAdbClientFactory? adbClientFactory = null,
        IConsoleIo? console = null,
        IEnvironmentVariables? environment = null,
        IUniqueIdGenerator? idGenerator = null,
        IDeviceHostFactory? deviceHostFactory = null,
        IViewSessionFactory? viewSessionFactory = null,
        IViewDoctorFactory? viewDoctorFactory = null,
        IViewProfileStore? viewProfileStore = null)
    {
        var resolvedTimeProvider = timeProvider ?? TimeProvider.System;
        var resolvedFileSystem = fileSystem ?? new PhysicalFileSystem();
        var processRunner1 = processRunner ?? new DefaultProcessRunner();
        var resolvedDelay = delay ?? new TaskDelay(resolvedTimeProvider);
        var resolvedConsole = console ?? new SystemConsoleIo();
        var resolvedEnvironment = environment ?? new SystemEnvironmentVariables();
        var idGenerator1 = idGenerator ?? new GuidUniqueIdGenerator();
        var resolvedAdbClientFactory = adbClientFactory ?? new DefaultAdbClientFactory();
        var resolvedDeviceHostFactory = deviceHostFactory ?? new DefaultDeviceHostFactory(
            resolvedAdbClientFactory,
            processRunner1,
            resolvedDelay,
            resolvedFileSystem,
            resolvedTimeProvider,
            resolvedEnvironment,
            idGenerator1);
        var resolvedViewSessionFactory = viewSessionFactory ?? new DefaultViewSessionFactory(
            resolvedConsole,
            resolvedTimeProvider,
            resolvedAdbClientFactory,
            processRunner1,
            resolvedEnvironment,
            resolvedFileSystem,
            idGenerator1);
        var resolvedViewDoctorFactory = viewDoctorFactory ?? new DefaultViewDoctorFactory(
            resolvedEnvironment,
            resolvedFileSystem,
            processRunner1);
        var resolvedViewProfileStore = viewProfileStore ?? new JsonViewProfileStore(resolvedFileSystem, resolvedEnvironment);
        var profileCoordinator = new ViewProfileCoordinator(resolvedViewProfileStore);
        var commandDispatcher = new AppCommandDispatcher(resolvedFileSystem, resolvedTimeProvider, resolvedDelay, resolvedEnvironment);
        var commandHost = new AppCommandHost(resolvedConsole, resolvedTimeProvider, profileCoordinator, commandDispatcher, resolvedViewDoctorFactory);
        var deviceHostLauncher = new DeviceHostLauncher(resolvedDeviceHostFactory);
        _executionShell = new AppExecutionShell(resolvedConsole, resolvedTimeProvider, commandHost);
        _commandFamilyRouter = new AppCommandFamilyRouter(
            resolvedTimeProvider,
            resolvedFileSystem,
            resolvedEnvironment,
            profileCoordinator,
            commandHost,
            new ViewSessionCommandPreparer(deviceHostLauncher, resolvedViewSessionFactory, profileCoordinator),
            new InspectSessionLauncher(deviceHostLauncher, resolvedConsole, resolvedTimeProvider),
            new ViewDoctorLauncher(deviceHostLauncher, commandHost),
            deviceHostLauncher);
    }

    /// <summary>
    /// Runs the command-line application.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>The process exit code.</returns>
    public Task<int> RunAsync(string[] args) => _executionShell.RunAsync(args, _commandFamilyRouter.DispatchAsync);
}
