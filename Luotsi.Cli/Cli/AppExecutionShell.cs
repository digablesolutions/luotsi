using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure;
using Luotsi.Cli.Models;
using Luotsi.Cli.Scenarios;

namespace Luotsi.Cli.Cli;

internal sealed class AppExecutionShell(
    IConsoleIo console,
    TimeProvider timeProvider,
    AppCommandHost commandHost)
{
    private readonly IConsoleIo _console = console ?? throw new ArgumentNullException(nameof(console));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly AppCommandHost _commandHost = commandHost ?? throw new ArgumentNullException(nameof(commandHost));

    public async Task<int> RunAsync(string[] args, Func<AppExecutionContext, Task<int>> dispatchAsync)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(dispatchAsync);

        var started = _timeProvider.GetUtcNow();
        var options = CliOptions.Parse(args);
        if (options.Command is null || options.HasFlag("help") || options.HasFlag("h"))
        {
            _console.WriteErrorLine(Help.Text);
            return options.HasFlag("help") || options.HasFlag("h") ? 0 : 2;
        }

        var context = new AppExecutionContext(started, options);

        try
        {
            return await dispatchAsync(context).ConfigureAwait(false);
        }
        catch (UsageException ex)
        {
            _commandHost.WriteUsageError(options.Command, started, context.CreateArtifactData(), ex);
            return 2;
        }
        catch (Exception ex)
        {
            var failure = ex as ICommandFailureDetails;
            var failureData = failure?.DataPayload;
            if (failureData is null && context.Runner is not null)
            {
                failureData = await context.Runner.CaptureFailureArtifactsAsync(new FailureCaptureRequest("command", options.Command, null, null, null, options.Command), ex).ConfigureAwait(false);
            }

            var category = failure?.CategoryOverride ?? ErrorInfo.Classify(ex.Message);
            _commandHost.WriteFailure(options.Command, started, failureData, context.CreateArtifactData(), ex, category);
            return 1;
        }
    }
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