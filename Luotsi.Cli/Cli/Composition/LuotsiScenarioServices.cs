using Luotsi.Cli.Cli.Routing;
using Luotsi.Cli.Scenarios;
using Microsoft.Extensions.DependencyInjection;

namespace Luotsi.Cli.Cli.Composition;

internal static class LuotsiScenarioServices
{
    public static IServiceCollection AddLuotsiScenarioRunner(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IScenarioTemplateResolver, ScenarioTemplateResolver>();
        services.AddSingleton<IScenarioMetricsCollector>(_ => CompositeScenarioMetricsCollector.CreateDefault());
        services.AddSingleton<ILabStateStore>(serviceProvider =>
            LabStateStoreFactory.Create(
                serviceProvider.GetRequiredService<Luotsi.Cli.Infrastructure.Contracts.IFileSystem>(),
                serviceProvider.GetRequiredService<Luotsi.Cli.Infrastructure.Contracts.IEnvironmentVariables>()));
        services.AddSingleton<ScenarioCatalog>();
        services.AddSingleton<ScenarioAuthoringService>();
        services.AddSingleton<ScenarioRunPlanner>();
        services.AddSingleton<ScenarioExecutorFactory>();
        services.AddSingleton<ScenarioBatchExecutorFactory>();
        services.AddSingleton<ScenarioValidationExecutorFactory>();
        services.AddSingleton<ScenarioRunEventCoordinatorFactory>();
        services.AddSingleton<ScenarioRunReportCoordinatorFactory>();
        services.AddSingleton<IScenarioDeviceAllocator, ScenarioDeviceAllocator>();
        services.AddSingleton<ScenarioDeviceHealthRegistry>();
        services.AddSingleton<ScenarioGovernancePolicyCoordinator>();
        services.AddSingleton<ScenarioRunOrchestrator>();
        services.AddSingleton<LabLeaseStore>();
        services.AddSingleton<LabLeaseClaimCoordinator>();
        services.AddSingleton<LabQuarantineStore>();
        services.AddSingleton<LabDeviceInventoryStore>();
        services.AddSingleton<ScenarioCommandDispatcher>();
        return services;
    }
}
