using Microsoft.Extensions.DependencyInjection;

namespace Luotsi.Cli.Cli.Composition;

internal static class LuotsiServiceCollection
{
    public static IServiceCollection AddLuotsiCli(this IServiceCollection services, AppDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(dependencies);

        services.AddSingleton(dependencies);
        return services
            .AddLuotsiInfrastructure(dependencies)
            .AddLuotsiScenarioRunner()
            .AddLuotsiReplayWorkbench()
            .AddLuotsiViewRuntime(dependencies)
            .AddLuotsiCommandRouting();
    }
}
