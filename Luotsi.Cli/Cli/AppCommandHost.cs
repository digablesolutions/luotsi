using System.Text.Json;
using System.Text.Json.Serialization;
using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure;
using Luotsi.Cli.Models;
using Luotsi.Cli.View;

namespace Luotsi.Cli.Cli;

internal sealed class AppCommandHost(
    IConsoleIo console,
    TimeProvider timeProvider,
    ViewProfileCoordinator profileCoordinator,
    AppCommandDispatcher commandDispatcher,
    IViewDoctorFactory viewDoctorFactory)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private readonly IConsoleIo _console = console ?? throw new ArgumentNullException(nameof(console));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly ViewProfileCoordinator _profileCoordinator = profileCoordinator ?? throw new ArgumentNullException(nameof(profileCoordinator));
    private readonly AppCommandDispatcher _commandDispatcher = commandDispatcher ?? throw new ArgumentNullException(nameof(commandDispatcher));
    private readonly IViewDoctorFactory _viewDoctorFactory = viewDoctorFactory ?? throw new ArgumentNullException(nameof(viewDoctorFactory));

    public async Task<int> RunProfileListAsync(CliOptions options, DateTimeOffset started, ArtifactSession artifacts)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(artifacts);

        var profiles = await _profileCoordinator.ListAsync().ConfigureAwait(false);
        WriteSuccess(options.Command!, started, new ViewProfileListResult(profiles), artifacts.ToData());
        return 0;
    }

    public async Task<int> RunProfileDeleteAsync(CliOptions options, DateTimeOffset started, ArtifactSession artifacts)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(artifacts);

        var profileName = options.Require("name");
        var deleted = await _profileCoordinator.DeleteAsync(profileName).ConfigureAwait(false);
        WriteSuccess(options.Command!, started, new ViewProfileDeleteResult(profileName, deleted), artifacts.ToData());
        return 0;
    }

    public async Task<int> RunViewDoctorAsync(CliOptions options, DateTimeOffset started, string adbExecutable, IDeviceHost runner, ArtifactSession artifacts)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(artifacts);

        var viewDoctor = _viewDoctorFactory.Create(runner);
        var report = await viewDoctor.DiagnoseAsync(ViewCommandOptionsFactory.Build(options, adbExecutable, allowJoinShare: false)).ConfigureAwait(false);
        WriteSuccess(options.Command!, started, report, artifacts.ToData());
        return 0;
    }

    public async Task<int> RunCommandAsync(CliOptions options, DateTimeOffset started, IDeviceHost runner, ArtifactSession artifacts)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(artifacts);

        var data = await _commandDispatcher.ExecuteAsync(options.Command!, options, runner).ConfigureAwait(false);
        WriteSuccess(options.Command!, started, data, artifacts.ToData());
        return 0;
    }

    public void WriteUsageError(string? command, DateTimeOffset started, ArtifactData artifacts, UsageException exception) =>
        WriteEnvelope(new CommandEnvelope(false, command, started, _timeProvider.GetUtcNow(), null, artifacts, ErrorInfo.From(exception, "usage_error")));

    public void WriteFailure(string? command, DateTimeOffset started, object? data, ArtifactData artifacts, Exception exception, string category) =>
        WriteEnvelope(new CommandEnvelope(false, command, started, _timeProvider.GetUtcNow(), data, artifacts, ErrorInfo.From(exception, category)));

    private void WriteSuccess(string command, DateTimeOffset started, object data, ArtifactData artifacts) =>
        WriteEnvelope(new CommandEnvelope(true, command, started, _timeProvider.GetUtcNow(), data, artifacts, null));

    private void WriteEnvelope(CommandEnvelope envelope) =>
        _console.WriteLine(JsonSerializer.Serialize(envelope, JsonOptions));
}