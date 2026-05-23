using Luotsi.Cli.Cli.Envelope;
using Luotsi.Cli.Cli.Provenance;
using Luotsi.Cli.Cli.Replay;
using Luotsi.Cli.Cli.Routing;
using Luotsi.Cli.Cli.Update;
using Luotsi.Cli.Cli.View;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Scenarios;

namespace Luotsi.Cli.Cli.Composition;

internal static class AppHostedCommandCompositionBuilder
{
    public static AppHostedCommandComposition Build(AppHostedCommandCompositionBuilderDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);

        var provenance = new BuildProvenanceProvider(dependencies.Environment).Create();
        var scenarioTemplateResolver = new ScenarioTemplateResolver(dependencies.TimeProvider, dependencies.Environment);
        var scenarioMetricsCollector = CompositeScenarioMetricsCollector.CreateDefault();
        var scenarioCatalog = new ScenarioCatalog(dependencies.FileSystem, scenarioTemplateResolver);
        var scenarioAuthoring = new ScenarioAuthoringService(dependencies.FileSystem, scenarioCatalog);
        var scenarioRunPlanner = new ScenarioRunPlanner(scenarioCatalog);
        var scenarioExecutorFactory = new ScenarioExecutorFactory(dependencies.FileSystem, dependencies.TimeProvider, dependencies.Delay, scenarioTemplateResolver, scenarioMetricsCollector);
        var scenarioBatchExecutorFactory = new ScenarioBatchExecutorFactory(scenarioExecutorFactory, scenarioMetricsCollector);
        var scenarioValidationExecutorFactory = new ScenarioValidationExecutorFactory(scenarioCatalog, dependencies.TimeProvider, scenarioMetricsCollector);
        var scenarioRunEventCoordinatorFactory = new ScenarioRunEventCoordinatorFactory(dependencies.FileSystem, dependencies.TimeProvider, provenance);
        var scenarioRunReportCoordinatorFactory = new ScenarioRunReportCoordinatorFactory(dependencies.FileSystem, dependencies.TimeProvider, provenance);
        var scenarioDeviceAllocator = new ScenarioDeviceAllocator();
        var scenarioRunOrchestrator = new ScenarioRunOrchestrator(
            scenarioRunPlanner,
            scenarioExecutorFactory,
            scenarioBatchExecutorFactory,
            scenarioValidationExecutorFactory,
            scenarioRunEventCoordinatorFactory,
            scenarioRunReportCoordinatorFactory,
            scenarioDeviceAllocator);
        var envelopeWriter = new AppCommandEnvelopeWriter(dependencies.Console, dependencies.TimeProvider, provenance);
        var jsonWriter = new AppCommandJsonWriter(dependencies.Console);
        var selfUpdateService = dependencies.SelfUpdateService
            ?? new SelfUpdateService(dependencies.FileSystem, dependencies.Environment, dependencies.ProcessRunner);
        var replayCommandDispatcher = new ReplayCommandDispatcher(dependencies.FileSystem);
        var commandDispatcher = new AppCommandDispatcher(
            new AdbSubcommandDispatcher(),
            new ScenarioCommandDispatcher(scenarioRunPlanner, scenarioRunOrchestrator, scenarioAuthoring),
            selfUpdateService,
            dependencies.ProfileCoordinator,
            new LabLeaseStore(dependencies.FileSystem, dependencies.TimeProvider));
        var replayTimelineService = new ReplayTimelineService(dependencies.FileSystem);
        var replayCommandHost = new ReplayCommandHost(new(
            envelopeWriter,
            jsonWriter,
            replayCommandDispatcher,
            dependencies.ProcessRunner,
            new ReplayScenarioDraftService(dependencies.FileSystem),
            new ReplaySearchService(dependencies.FileSystem),
            new ReplayCapsuleService(dependencies.FileSystem),
            replayTimelineService,
            new ReplayGraphService(dependencies.FileSystem, replayTimelineService),
            new ReplayClusterService(dependencies.FileSystem)));

        return new(
            envelopeWriter,
            replayCommandHost,
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
    IProcessRunner ProcessRunner,
    IDelay Delay,
    ISelfUpdateService? SelfUpdateService,
    ViewProfileCoordinator ProfileCoordinator);

internal sealed record AppHostedCommandComposition(
    AppCommandEnvelopeWriter EnvelopeWriter,
    ReplayCommandHost ReplayCommandHost,
    AppCommandHost CommandHost);
