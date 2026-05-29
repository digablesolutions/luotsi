using Luotsi.Cli.Cli.Composition;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Models;
using Luotsi.Cli.Scenarios;

namespace Luotsi.Cli.Cli.Envelope;

internal sealed class AppCommandFailureResponder(AppCommandEnvelopeWriter envelopeWriter)
{
    private readonly AppCommandEnvelopeWriter _envelopeWriter = envelopeWriter ?? throw new ArgumentNullException(nameof(envelopeWriter));

    public int WriteUsageError(CliOptions options, DateTimeOffset started, ArtifactData artifacts, UsageException exception)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(exception);
        _envelopeWriter.WriteUsageError(
            options.Command,
            started,
            artifacts,
            exception,
            AppCommandConsoleOutputModeResolver.ResolveForFailure(options));
        return 2;
    }

    public async Task<int> WriteFailureAsync(string? command, DateTimeOffset started, AppExecutionContext context, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(exception);

        var failure = exception as ICommandFailureDetails;
        var failureData = failure?.DataPayload;
        if (failureData is null && context.Runner is not null)
        {
            failureData = await context.Runner.CaptureFailureArtifactsAsync(
                new FailureCaptureRequest("command", command, null, null, null, command),
                exception).ConfigureAwait(false);
        }

        var category = failure?.CategoryOverride ?? ErrorInfo.Classify(exception.Message);
        _envelopeWriter.WriteFailure(
            command,
            started,
            failureData,
            context.CreateArtifactData(),
            exception,
            category,
            AppCommandConsoleOutputModeResolver.ResolveForFailure(context.Options));
        return failureData is ScenarioRunFailureData { CiPolicy: { ExitCodeApplied: true } ciPolicy }
            ? ciPolicy.RecommendedExitCode
            : 1;
    }
}
