using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli;

internal sealed class AppExecutionShell(AppExecutionShellDependencies dependencies)
{
    private readonly AppExecutionShellDependencies _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));

    public async Task<int> RunAsync(string[] args, Func<AppExecutionContext, Task<int>> dispatchAsync)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(dispatchAsync);

        var started = _dependencies.TimeProvider.GetUtcNow();
        var options = CliOptions.Parse(args);
        if (options.Command is null || options.HasFlag("help") || options.HasFlag("h"))
        {
            _dependencies.Console.WriteErrorLine(Help.Text);
            return options.HasFlag("help") || options.HasFlag("h") ? 0 : 2;
        }

        var context = new AppExecutionContext(started, options);

        try
        {
            return await dispatchAsync(context).ConfigureAwait(false);
        }
        catch (UsageException ex)
        {
            return _dependencies.FailureResponder.WriteUsageError(options.Command, started, context.CreateArtifactData(), ex);
        }
        catch (Exception ex)
        {
            return await _dependencies.FailureResponder.WriteFailureAsync(options.Command, started, context, ex).ConfigureAwait(false);
        }
    }
}

internal sealed class AppExecutionShellDependencies
{
    public required IConsoleIo Console { get; init; }

    public required TimeProvider TimeProvider { get; init; }

    public required AppCommandFailureResponder FailureResponder { get; init; }
}

internal sealed class AppExecutionContext(DateTimeOffset started, CliOptions options)
{
    public DateTimeOffset Started { get; } = started;

    public CliOptions Options { get; } = options ?? throw new ArgumentNullException(nameof(options));

    public ArtifactSession? Artifacts { get; set; }

    public IDeviceHost? Runner { get; set; }

    public ArtifactData CreateArtifactData() => Artifacts?.ToData() ?? new ArtifactData(
        Options.Get("artifacts") ?? string.Empty,
        Options.Get("poll-artifacts") ?? CliDefaults.DefaultPollArtifactsPolicy);
}