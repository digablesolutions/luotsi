using Luotsi.Cli.Cli.Doctor;
using Luotsi.Cli.Cli.Inspect;
using Luotsi.Cli.Cli.View;
using Luotsi.Cli.View.Contracts;
using Luotsi.Cli.View.Diagnostics;
using Luotsi.Cli.View.Session;
using Microsoft.Extensions.DependencyInjection;

namespace Luotsi.Cli.Cli.Composition;

internal static class LuotsiViewServices
{
    public static IServiceCollection AddLuotsiViewRuntime(this IServiceCollection services, AppDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(dependencies);

        services.AddSingleton<IViewSessionFactory>(serviceProvider =>
            dependencies.ViewSessionFactory ?? ActivatorUtilities.CreateInstance<DefaultViewSessionFactory>(serviceProvider));
        services.AddSingleton<IViewDoctorFactory>(serviceProvider =>
            dependencies.ViewDoctorFactory ?? ActivatorUtilities.CreateInstance<DefaultViewDoctorFactory>(serviceProvider));
        services.AddSingleton<IViewSetupFactory>(serviceProvider =>
            dependencies.ViewSetupFactory ?? ActivatorUtilities.CreateInstance<DefaultViewSetupFactory>(serviceProvider));
        services.AddSingleton<FfmpegSetupProvisioner>();
        services.AddSingleton<ViewSessionCommandPreparer>();
        services.AddSingleton<ViewDiagnosticCommandHost>();
        services.AddSingleton<ViewDiagnosticsLauncher>();
        services.AddSingleton<DoctorCommandHost>();
        services.AddSingleton<DoctorCommandLauncher>();
        services.AddSingleton<InspectSessionLauncher>();
        return services;
    }
}
