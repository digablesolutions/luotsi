using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Cli.Envelope;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli.Composition;

internal sealed class AppExecutionShell(
    IConsoleIo console,
    TimeProvider timeProvider,
    AppCommandFailureResponder failureResponder)
{
    private readonly IConsoleIo _console = console ?? throw new ArgumentNullException(nameof(console));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly AppCommandFailureResponder _failureResponder = failureResponder ?? throw new ArgumentNullException(nameof(failureResponder));

    public async Task<int> RunAsync(string[] args, Func<AppExecutionContext, Task<int>> dispatchAsync)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(dispatchAsync);

        var started = _timeProvider.GetUtcNow();
        var options = CliOptions.Parse(args);
        if (options.Command is null && options.HasFlag("version"))
        {
            _console.WriteLine($"luotsi {AppVersion.GetDisplayVersion()}");
            return 0;
        }

        if (string.Equals(options.Command, "help", StringComparison.OrdinalIgnoreCase))
        {
            return WriteHelp(options);
        }

        if (options.Command is null || options.HasFlag("help") || options.HasFlag("h"))
        {
            _console.WriteErrorLine(options.Command is null ? Help.Text : Help.GetTopic(options.Command));
            return options.HasFlag("help") || options.HasFlag("h") ? 0 : 2;
        }

        var context = new AppExecutionContext(started, options);

        try
        {
            if (ShouldValidateCommandEnvelopeOutput(options.Command))
            {
                _ = AppCommandConsoleOutputModeResolver.Resolve(options);
            }

            return await dispatchAsync(context).ConfigureAwait(false);
        }
        catch (UsageException ex)
        {
            return _failureResponder.WriteUsageError(options, started, context.CreateArtifactData(), ex);
        }
        catch (Exception ex)
        {
            return await _failureResponder.WriteFailureAsync(options.Command, started, context, ex).ConfigureAwait(false);
        }
    }

    private int WriteHelp(CliOptions options)
    {
        if (options.Arguments.Count == 0)
        {
            _console.WriteErrorLine(Help.Text);
            return 0;
        }

        var topic = options.Arguments[0];
        if (!Help.TryGetTopic(topic, out var text))
        {
            _console.WriteErrorLine($"Unknown help topic '{topic}'. Available topics: {string.Join(", ", Help.SuggestedTopics)}.");
            return 2;
        }

        _console.WriteErrorLine(text);
        return 0;
    }

    private static bool ShouldValidateCommandEnvelopeOutput(string? command) =>
        !string.Equals(command, "view", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(command, "reconnect", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(command, "inspect", StringComparison.OrdinalIgnoreCase);
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
