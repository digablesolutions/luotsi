using System.Text.Json;
using System.Text.Json.Serialization;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli.Envelope;

internal sealed class AppCommandEnvelopeWriter(IConsoleIo console, TimeProvider timeProvider, BuildProvenance provenance)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private readonly IConsoleIo _console = console ?? throw new ArgumentNullException(nameof(console));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly BuildProvenance _provenance = provenance ?? throw new ArgumentNullException(nameof(provenance));

    public void WriteSuccess(string command, DateTimeOffset started, object data, ArtifactData artifacts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentNullException.ThrowIfNull(data);

        WriteEnvelope(new CommandEnvelope(true, command, started, _timeProvider.GetUtcNow(), data, artifacts, _provenance, null));
    }

    public void WriteUsageError(string? command, DateTimeOffset started, ArtifactData artifacts, UsageException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        WriteEnvelope(new CommandEnvelope(false, command, started, _timeProvider.GetUtcNow(), null, artifacts, _provenance, ErrorInfo.From(exception, "usage_error")));
    }

    public void WriteFailure(string? command, DateTimeOffset started, object? data, ArtifactData artifacts, Exception exception, string category)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);

        WriteEnvelope(new CommandEnvelope(false, command, started, _timeProvider.GetUtcNow(), data, artifacts, _provenance, ErrorInfo.From(exception, category)));
    }

    private void WriteEnvelope(CommandEnvelope envelope) =>
        _console.WriteLine(JsonSerializer.Serialize(envelope, JsonOptions));
}
