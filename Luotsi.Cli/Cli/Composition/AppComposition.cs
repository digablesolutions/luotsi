using Microsoft.Extensions.DependencyInjection;
using Luotsi.Cli.Cli.Routing;

namespace Luotsi.Cli.Cli.Composition;

internal sealed class AppComposition : IDisposable
{
    private readonly ServiceProvider _serviceProvider;

    private AppComposition(ServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        ExecutionShell = _serviceProvider.GetRequiredService<AppExecutionShell>();
        CommandFamilyRouter = _serviceProvider.GetRequiredService<AppCommandFamilyRouter>();
    }

    public AppExecutionShell ExecutionShell { get; }

    public AppCommandFamilyRouter CommandFamilyRouter { get; }

    public static AppComposition Create(AppDependencies? dependencies = null)
    {
        var services = new ServiceCollection();
        services.AddLuotsiCli(dependencies ?? new AppDependencies());
        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        return new AppComposition(provider);
    }

    public void Dispose() => _serviceProvider.Dispose();
}
