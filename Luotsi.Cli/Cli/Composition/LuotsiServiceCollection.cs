using Luotsi.Cli.Cli.Doctor;
using Luotsi.Cli.Cli.Envelope;
using Luotsi.Cli.Cli.Hosting;
using Luotsi.Cli.Cli.Inspect;
using Luotsi.Cli.Cli.Provenance;
using Luotsi.Cli.Cli.Replay;
using Luotsi.Cli.Cli.Routing;
using Luotsi.Cli.Cli.Update;
using Luotsi.Cli.Cli.View;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Infrastructure.Devices;
using Luotsi.Cli.Infrastructure.Ids;
using Luotsi.Cli.Infrastructure.Processes;
using Luotsi.Cli.Infrastructure.System;
using Luotsi.Cli.Infrastructure.Time;
using Luotsi.Cli.Scenarios;
using Luotsi.Cli.View.Contracts;
using Luotsi.Cli.View.Diagnostics;
using Luotsi.Cli.View.Session;
using Microsoft.Extensions.DependencyInjection;

namespace Luotsi.Cli.Cli.Composition;

internal static class LuotsiServiceCollection
{
    public static IServiceCollection AddLuotsiCli(this IServiceCollection services, AppDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(dependencies);

        services.AddSingleton(dependencies);
        RegisterInfrastructure(services, dependencies);
        RegisterScenarioServices(services);
        RegisterReplayServices(services);
        RegisterCommandServices(services);
        RegisterViewServices(services, dependencies);
        RegisterRuntime(services);
        return services;
    }

    private static void RegisterInfrastructure(IServiceCollection services, AppDependencies dependencies)
    {
        services.AddSingleton(dependencies.TimeProvider ?? TimeProvider.System);
        services.AddSingleton(dependencies.FileSystem ?? new PhysicalFileSystem());
        services.AddSingleton(dependencies.ProcessRunner ?? new DefaultProcessRunner());
        services.AddSingleton<IDelay>(serviceProvider => dependencies.Delay ?? new TaskDelay(serviceProvider.GetRequiredService<TimeProvider>()));
        services.AddSingleton(dependencies.Console ?? new SystemConsoleIo());
        services.AddSingleton(dependencies.Environment ?? new SystemEnvironmentVariables());
        services.AddSingleton(dependencies.IdGenerator ?? new GuidUniqueIdGenerator());
        services.AddSingleton(dependencies.AdbClientFactory ?? new DefaultAdbClientFactory());
        services.AddSingleton<IDeviceHostFactory>(serviceProvider =>
            dependencies.DeviceHostFactory ?? ActivatorUtilities.CreateInstance<DefaultDeviceHostFactory>(serviceProvider));
        services.AddSingleton<IViewProfileStore>(serviceProvider =>
            dependencies.ViewProfileStore ?? ActivatorUtilities.CreateInstance<JsonViewProfileStore>(serviceProvider));
        services.AddSingleton<ViewProfileCoordinator>();
        services.AddSingleton<DeviceHostLauncher>();
        services.AddSingleton<BuildProvenanceProvider>();
        services.AddSingleton(serviceProvider => serviceProvider.GetRequiredService<BuildProvenanceProvider>().Create());
    }

    private static void RegisterScenarioServices(IServiceCollection services)
    {
        services.AddSingleton<IScenarioTemplateResolver, ScenarioTemplateResolver>();
        services.AddSingleton<IScenarioMetricsCollector>(_ => CompositeScenarioMetricsCollector.CreateDefault());
        services.AddSingleton<ScenarioCatalog>();
        services.AddSingleton<ScenarioAuthoringService>();
        services.AddSingleton<ScenarioRunPlanner>();
        services.AddSingleton<ScenarioExecutorFactory>();
        services.AddSingleton<ScenarioBatchExecutorFactory>();
        services.AddSingleton<ScenarioValidationExecutorFactory>();
        services.AddSingleton<ScenarioRunEventCoordinatorFactory>();
        services.AddSingleton<ScenarioRunReportCoordinatorFactory>();
        services.AddSingleton<IScenarioDeviceAllocator, ScenarioDeviceAllocator>();
        services.AddSingleton<ScenarioRunOrchestrator>();
        services.AddSingleton<LabLeaseStore>();
        services.AddSingleton<LabQuarantineStore>();
        services.AddSingleton<ScenarioCommandDispatcher>();
    }

    private static void RegisterReplayServices(IServiceCollection services)
    {
        services.AddSingleton<ReplayCommandDispatcher>();
        services.AddSingleton<ReplayScenarioDraftService>();
        services.AddSingleton<ReplaySearchService>();
        services.AddSingleton<ReplayCapsuleService>();
        services.AddSingleton<ReplayTimelineService>();
        services.AddSingleton<ReplayScrubService>();
        services.AddSingleton<ReplayGraphService>();
        services.AddSingleton<ReplayClusterService>();
        services.AddSingleton<ReplayCommandHostDependencies>();
        services.AddSingleton<ReplayCommandHost>();
    }

    private static void RegisterCommandServices(IServiceCollection services)
    {
        services.AddSingleton<ISelfUpdateService>(serviceProvider =>
        {
            var dependencies = serviceProvider.GetRequiredService<AppDependencies>();
            return dependencies.SelfUpdateService
                ?? ActivatorUtilities.CreateInstance<SelfUpdateService>(serviceProvider);
        });
        services.AddSingleton<AppCommandEnvelopeWriter>();
        services.AddSingleton<AppCommandJsonWriter>();
        services.AddSingleton<AppCommandFailureResponder>();
        services.AddSingleton<AppCommandExitCodeResolver>();
        services.AddSingleton<AdbSubcommandDispatcher>();
        services.AddSingleton<AppCommandDispatcher>();
        services.AddSingleton<AppCommandHostDependencies>();
        services.AddSingleton<AppCommandHost>();
        services.AddSingleton(serviceProvider => new AppCommandRouteBootstrapperDependencies
        {
            TimeProvider = serviceProvider.GetRequiredService<TimeProvider>(),
            FileSystem = serviceProvider.GetRequiredService<IFileSystem>(),
            Environment = serviceProvider.GetRequiredService<IEnvironmentVariables>(),
            ProfileCoordinator = serviceProvider.GetRequiredService<ViewProfileCoordinator>(),
            DeviceHostLauncher = serviceProvider.GetRequiredService<DeviceHostLauncher>(),
            LabLeaseStore = serviceProvider.GetRequiredService<LabLeaseStore>(),
            LabQuarantineStore = serviceProvider.GetRequiredService<LabQuarantineStore>()
        });
        services.AddSingleton<AppCommandRouteBootstrapper>();
    }

    private static void RegisterViewServices(IServiceCollection services, AppDependencies dependencies)
    {
        services.AddSingleton<IViewSessionFactory>(serviceProvider =>
            dependencies.ViewSessionFactory ?? ActivatorUtilities.CreateInstance<DefaultViewSessionFactory>(serviceProvider));
        services.AddSingleton<IViewDoctorFactory>(serviceProvider =>
            dependencies.ViewDoctorFactory ?? ActivatorUtilities.CreateInstance<DefaultViewDoctorFactory>(serviceProvider));
        services.AddSingleton<IViewSetupFactory>(serviceProvider =>
            dependencies.ViewSetupFactory ?? ActivatorUtilities.CreateInstance<DefaultViewSetupFactory>(serviceProvider));
        services.AddSingleton<FfmpegSetupProvisioner>();
        services.AddSingleton<ViewSessionCommandPreparer>();
        services.AddSingleton<ViewDiagnosticCommandHostDependencies>();
        services.AddSingleton<ViewDiagnosticCommandHost>();
        services.AddSingleton<ViewDiagnosticsLauncher>();
        services.AddSingleton<DoctorCommandHostDependencies>();
        services.AddSingleton<DoctorCommandHost>();
        services.AddSingleton<DoctorCommandLauncher>();
        services.AddSingleton<InspectSessionLauncher>();
    }

    private static void RegisterRuntime(IServiceCollection services)
    {
        services.AddSingleton(serviceProvider => new AppExecutionShellDependencies
        {
            Console = serviceProvider.GetRequiredService<IConsoleIo>(),
            TimeProvider = serviceProvider.GetRequiredService<TimeProvider>(),
            FailureResponder = serviceProvider.GetRequiredService<AppCommandFailureResponder>()
        });
        services.AddSingleton<AppExecutionShell>();
        services.AddSingleton(serviceProvider => new AppCommandFamilyRouterDependencies
        {
            RouteBootstrapper = serviceProvider.GetRequiredService<AppCommandRouteBootstrapper>(),
            CommandHost = serviceProvider.GetRequiredService<AppCommandHost>(),
            ReplayCommandHost = serviceProvider.GetRequiredService<ReplayCommandHost>(),
            ViewSessionCommandPreparer = serviceProvider.GetRequiredService<ViewSessionCommandPreparer>(),
            InspectSessionLauncher = serviceProvider.GetRequiredService<InspectSessionLauncher>(),
            ViewDiagnosticsLauncher = serviceProvider.GetRequiredService<ViewDiagnosticsLauncher>(),
            DoctorCommandLauncher = serviceProvider.GetRequiredService<DoctorCommandLauncher>()
        });
        services.AddSingleton<AppCommandFamilyRouter>();
    }
}
