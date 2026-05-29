using Luotsi.Cli.Cli.Replay;
using Luotsi.Cli.Cli.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Luotsi.Cli.Cli.Composition;

internal static class LuotsiReplayServices
{
    public static IServiceCollection AddLuotsiReplayWorkbench(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

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
        return services;
    }
}
