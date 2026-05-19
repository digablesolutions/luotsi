using Luotsi.Cli.Cli.Hosting;
using Luotsi.Cli.Cli.View;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Infrastructure.Devices;
using Luotsi.Cli.Infrastructure.Ids;
using Luotsi.Cli.Infrastructure.Processes;
using Luotsi.Cli.Infrastructure.System;
using Luotsi.Cli.Infrastructure.Time;

namespace Luotsi.Cli.Cli.Composition;

internal static class AppInfrastructureCompositionBuilder
{
    public static AppInfrastructureComposition Build(AppDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);

        var timeProvider = dependencies.TimeProvider ?? TimeProvider.System;
        var fileSystem = dependencies.FileSystem ?? new PhysicalFileSystem();
        var processRunner = dependencies.ProcessRunner ?? new DefaultProcessRunner();
        var delay = dependencies.Delay ?? new TaskDelay(timeProvider);
        var console = dependencies.Console ?? new SystemConsoleIo();
        var environment = dependencies.Environment ?? new SystemEnvironmentVariables();
        var idGenerator = dependencies.IdGenerator ?? new GuidUniqueIdGenerator();
        var adbClientFactory = dependencies.AdbClientFactory ?? new DefaultAdbClientFactory();
        var deviceHostFactory = dependencies.DeviceHostFactory ?? new DefaultDeviceHostFactory(
            adbClientFactory,
            processRunner,
            delay,
            fileSystem,
            timeProvider,
            environment,
            idGenerator);
        var viewProfileStore = dependencies.ViewProfileStore ?? new JsonViewProfileStore(fileSystem, environment);
        var profileCoordinator = new ViewProfileCoordinator(viewProfileStore);

        return new(
            timeProvider,
            fileSystem,
            processRunner,
            delay,
            console,
            environment,
            idGenerator,
            adbClientFactory,
            profileCoordinator,
            new DeviceHostLauncher(deviceHostFactory, environment));
    }
}

internal sealed record AppInfrastructureComposition(
    TimeProvider TimeProvider,
    IFileSystem FileSystem,
    IProcessRunner ProcessRunner,
    IDelay Delay,
    IConsoleIo Console,
    IEnvironmentVariables Environment,
    IUniqueIdGenerator IdGenerator,
    IAdbClientFactory AdbClientFactory,
    ViewProfileCoordinator ProfileCoordinator,
    DeviceHostLauncher DeviceHostLauncher);