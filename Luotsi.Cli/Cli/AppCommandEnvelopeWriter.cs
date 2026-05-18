using System.Text.Json;
using System.Text.Json.Serialization;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli;

internal sealed class AppCommandEnvelopeWriter(IConsoleIo console, TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private readonly IConsoleIo _console = console ?? throw new ArgumentNullException(nameof(console));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public void WriteSuccess(string command, DateTimeOffset started, object data, ArtifactData artifacts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentNullException.ThrowIfNull(data);

        WriteEnvelope(new CommandEnvelope(true, command, started, _timeProvider.GetUtcNow(), data, artifacts, null));
    }

    public void WriteUsageError(string? command, DateTimeOffset started, ArtifactData artifacts, UsageException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        WriteEnvelope(new CommandEnvelope(false, command, started, _timeProvider.GetUtcNow(), null, artifacts, ErrorInfo.From(exception, "usage_error")));
    }

    public void WriteFailure(string? command, DateTimeOffset started, object? data, ArtifactData artifacts, Exception exception, string category)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);

        WriteEnvelope(new CommandEnvelope(false, command, started, _timeProvider.GetUtcNow(), data, artifacts, ErrorInfo.From(exception, category)));
    }

    private void WriteEnvelope(CommandEnvelope envelope) =>
        _console.WriteLine(JsonSerializer.Serialize(envelope, JsonOptions));
}