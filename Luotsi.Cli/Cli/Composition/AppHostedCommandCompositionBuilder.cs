using Luotsi.Cli.Cli.Envelope;
using Luotsi.Cli.Cli.Routing;
using Luotsi.Cli.Cli.View;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Scenarios;

namespace Luotsi.Cli.Cli.Composition;

internal static class AppHostedCommandCompositionBuilder
{
    public static AppHostedCommandComposition Build(AppHostedCommandCompositionBuilderDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);

        var scenarioTemplateResolver = new ScenarioTemplateResolver(dependencies.TimeProvider, dependencies.Environment);
        var scenarioCatalog = new ScenarioCatalog(dependencies.FileSystem, scenarioTemplateResolver);
        var scenarioRunPlanner = new ScenarioRunPlanner(scenarioCatalog);
        var scenarioExecutorFactory = new ScenarioExecutorFactory(dependencies.FileSystem, dependencies.TimeProvider, dependencies.Delay, scenarioTemplateResolver);
        var scenarioBatchExecutorFactory = new ScenarioBatchExecutorFactory(scenarioExecutorFactory);
        var scenarioRunEventCoordinatorFactory = new ScenarioRunEventCoordinatorFactory(dependencies.FileSystem, dependencies.TimeProvider);
        var scenarioRunReportCoordinatorFactory = new ScenarioRunReportCoordinatorFactory(dependencies.FileSystem, dependencies.TimeProvider);
        var envelopeWriter = new AppCommandEnvelopeWriter(dependencies.Console, dependencies.TimeProvider);
        var commandDispatcher = new AppCommandDispatcher(
            new AdbSubcommandDispatcher(),
            new ScenarioCommandDispatcher(scenarioRunPlanner, scenarioExecutorFactory, scenarioBatchExecutorFactory, scenarioRunEventCoordinatorFactory, scenarioRunReportCoordinatorFactory),
            dependencies.ProfileCoordinator);

        return new(
            envelopeWriter,
            new AppCommandHost(new(
                envelopeWriter,
                new AppCommandExitCodeResolver(),
                dependencies.ProfileCoordinator,
                commandDispatcher)));
    }
}

internal sealed record AppHostedCommandCompositionBuilderDependencies(
    TimeProvider TimeProvider,
    IConsoleIo Console,
    IFileSystem FileSystem,
    IEnvironmentVariables Environment,
    IDelay Delay,
    ViewProfileCoordinator ProfileCoordinator);

internal sealed record AppHostedCommandComposition(
    AppCommandEnvelopeWriter EnvelopeWriter,
    AppCommandHost CommandHost);
