using Luotsi.Cli.Cli.Hosting;
using Luotsi.Cli.Cli.Provenance;
using Luotsi.Cli.Cli.View;
using Luotsi.Cli.Hosts.Android;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Infrastructure.Devices;
using Luotsi.Cli.Infrastructure.Ids;
using Luotsi.Cli.Infrastructure.Processes;
using Luotsi.Cli.Infrastructure.Resilience;
using Luotsi.Cli.Infrastructure.System;
using Luotsi.Cli.Infrastructure.Time;
using Luotsi.Cli.Models;
using Luotsi.Cli.View.Contracts;
using Luotsi.Cli.View.Session;
using Microsoft.Extensions.DependencyInjection;
using Polly;

namespace Luotsi.Cli.Cli.Composition;

internal static class LuotsiInfrastructureServices
{
    public static IServiceCollection AddLuotsiInfrastructure(this IServiceCollection services, AppDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(dependencies);

        services.AddSingleton(dependencies.TimeProvider ?? TimeProvider.System);
        services.AddSingleton(dependencies.FileSystem ?? new PhysicalFileSystem());
        services.AddSingleton(dependencies.ProcessRunner ?? new DefaultProcessRunner());
        services.AddSingleton<IDelay>(serviceProvider => dependencies.Delay ?? new TaskDelay(serviceProvider.GetRequiredService<TimeProvider>()));
        services.AddSingleton(dependencies.Console ?? new SystemConsoleIo());
        services.AddSingleton(dependencies.Environment ?? new SystemEnvironmentVariables());
        services.AddSingleton(dependencies.IdGenerator ?? new GuidUniqueIdGenerator());
        services.AddSingleton<IArtifactFolderOpener, SystemArtifactFolderOpener>();
        services.AddResiliencePipeline(AdbResilience.CommandRetryPipelineName, builder =>
            builder.AddRetry(AdbResilience.CreateCommandRetryOptions()));
        services.AddResiliencePipeline(LuotsiResiliencePipelines.SetupDownloadPipelineName, builder =>
            builder.AddRetry(LuotsiResiliencePipelines.CreateSetupDownloadRetryOptions()));
        services.AddResiliencePipeline(LuotsiResiliencePipelines.LabProbePipelineName, builder =>
            builder.AddRetry(LuotsiResiliencePipelines.CreateLabProbeRetryOptions()));
        if (dependencies.AdbClientFactory is not null)
        {
            services.AddSingleton(dependencies.AdbClientFactory);
        }
        else
        {
            services.AddSingleton<IAdbClientFactory>(serviceProvider =>
                ActivatorUtilities.CreateInstance<DefaultAdbClientFactory>(serviceProvider));
        }

        services.AddSingleton<IDeviceHostFactory>(serviceProvider =>
            dependencies.DeviceHostFactory ?? ActivatorUtilities.CreateInstance<DefaultDeviceHostFactory>(serviceProvider));
        services.AddSingleton<IViewProfileStore>(serviceProvider =>
            dependencies.ViewProfileStore ?? ActivatorUtilities.CreateInstance<JsonViewProfileStore>(serviceProvider));
        services.AddSingleton<ViewProfileCoordinator>();
        services.AddSingleton<DeviceHostLauncher>();
        services.AddSingleton<BuildProvenanceProvider>();
        services.AddSingleton<BuildProvenance>(serviceProvider => serviceProvider.GetRequiredService<BuildProvenanceProvider>().Create());
        return services;
    }
}
