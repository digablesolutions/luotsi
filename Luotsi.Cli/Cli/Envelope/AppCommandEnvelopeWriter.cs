using System.Text.Json;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli.Envelope;

internal sealed class AppCommandEnvelopeWriter(IConsoleIo console, TimeProvider timeProvider, BuildProvenance provenance)
{
    private readonly IConsoleIo _console = console ?? throw new ArgumentNullException(nameof(console));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly BuildProvenance _provenance = provenance ?? throw new ArgumentNullException(nameof(provenance));
    private readonly AppCommandHumanFormatter _humanFormatter = new(console);

    public void WriteSuccess(string command, DateTimeOffset started, object data, ArtifactData artifacts, AppCommandConsoleOutputMode outputMode = AppCommandConsoleOutputMode.Json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentNullException.ThrowIfNull(data);

        WriteEnvelope(new CommandEnvelope(true, command, started, _timeProvider.GetUtcNow(), data, artifacts, _provenance, null), outputMode);
    }

    public void WriteUsageError(string? command, DateTimeOffset started, ArtifactData artifacts, UsageException exception, AppCommandConsoleOutputMode outputMode = AppCommandConsoleOutputMode.Json)
    {
        ArgumentNullException.ThrowIfNull(exception);
        WriteEnvelope(new CommandEnvelope(false, command, started, _timeProvider.GetUtcNow(), null, artifacts, _provenance, ErrorInfo.From(exception, "usage_error")), outputMode);
    }

    public void WriteFailure(string? command, DateTimeOffset started, object? data, ArtifactData artifacts, Exception exception, string category, AppCommandConsoleOutputMode outputMode = AppCommandConsoleOutputMode.Json)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);

        WriteEnvelope(new CommandEnvelope(false, command, started, _timeProvider.GetUtcNow(), data, artifacts, _provenance, ErrorInfo.From(exception, category)), outputMode);
    }

    private void WriteEnvelope(CommandEnvelope envelope, AppCommandConsoleOutputMode outputMode)
    {
        if (outputMode == AppCommandConsoleOutputMode.Quiet && envelope.Ok)
        {
            return;
        }

        if (outputMode == AppCommandConsoleOutputMode.Human)
        {
            _humanFormatter.Write(envelope);
            return;
        }

        _console.WriteLine(JsonSerializer.Serialize(envelope, AppCommandJson.Options));
    }
}
