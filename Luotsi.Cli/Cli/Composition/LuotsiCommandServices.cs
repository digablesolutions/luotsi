using Luotsi.Cli.Cli.Envelope;
using Luotsi.Cli.Cli.Routing;
using Luotsi.Cli.Cli.Update;
using Luotsi.Cli.Artifacts;
using Microsoft.Extensions.DependencyInjection;

namespace Luotsi.Cli.Cli.Composition;

internal static class LuotsiCommandServices
{
    public static IServiceCollection AddLuotsiCommandRouting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

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
        services.AddSingleton<ArtifactCommandService>();
        services.AddSingleton<ArtifactCommandHost>();
        services.AddSingleton<AppCommandHost>();
        services.AddSingleton<AppCommandRouteBootstrapper>();
        services.AddSingleton<AppExecutionShell>();
        services.AddSingleton<AppCommandFamilyRouter>();
        return services;
    }
}
