using Luotsi.Cli.Cli.Composition;
using Luotsi.Cli.Cli.Routing;

namespace Luotsi.Cli.Cli;

/// <summary>
/// Entry point for the Luotsi command-line application.
/// </summary>
public sealed class App : IDisposable
{
    private readonly AppComposition _composition;
    private readonly AppExecutionShell _executionShell;
    private readonly AppCommandFamilyRouter _commandFamilyRouter;

    /// <summary>
    /// Creates the CLI application with default services.
    /// </summary>
    public App()
        : this(null)
    {
    }

    /// <summary>
    /// Creates the CLI application with optional service overrides.
    /// </summary>
    /// <param name="dependencies">Optional dependency overrides for tests or specialized hosting.</param>
    public App(AppDependencies? dependencies)
    {
        _composition = AppComposition.Create(dependencies);
        _executionShell = _composition.ExecutionShell;
        _commandFamilyRouter = _composition.CommandFamilyRouter;
    }

    /// <summary>
    /// Runs the command-line application.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>The process exit code.</returns>
    public Task<int> RunAsync(string[] args) => _executionShell.RunAsync(args, _commandFamilyRouter.DispatchAsync);

    /// <inheritdoc />
    public void Dispose() => _composition.Dispose();
}
