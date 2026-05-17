using System.Text.Json;
using System.Text.Json.Serialization;
using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;
using Luotsi.Cli.View.Diagnostics;

namespace Luotsi.Cli.Cli;

internal sealed class AppCommandHost(AppCommandHostDependencies dependencies)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private readonly AppCommandHostDependencies _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));

    public async Task<int> RunProfileListAsync(CliOptions options, DateTimeOffset started, ArtifactSession artifacts)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(artifacts);

        var profiles = await _dependencies.ProfileCoordinator.ListAsync().ConfigureAwait(false);
        WriteSuccess(options.Command!, started, new ViewProfileListResult(profiles), artifacts.ToData());
        return 0;
    }

    public async Task<int> RunProfileDeleteAsync(CliOptions options, DateTimeOffset started, ArtifactSession artifacts)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(artifacts);

        var profileName = options.Require("name");
        var deleted = await _dependencies.ProfileCoordinator.DeleteAsync(profileName).ConfigureAwait(false);
        WriteSuccess(options.Command!, started, new ViewProfileDeleteResult(profileName, deleted), artifacts.ToData());
        return 0;
    }

    public async Task<int> RunViewDoctorAsync(CliOptions options, DateTimeOffset started, string adbExecutable, IDeviceHost runner, ArtifactSession artifacts)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(artifacts);

        var viewDoctor = _dependencies.ViewDoctorFactory.Create(runner);
        var report = await viewDoctor.DiagnoseAsync(ViewCommandOptionsFactory.Build(options, adbExecutable, allowJoinShare: false)).ConfigureAwait(false);
        WriteSuccess(options.Command!, started, report, artifacts.ToData());
        return 0;
    }

    public async Task<int> RunCommandAsync(CliOptions options, DateTimeOffset started, string adbExecutable, IDeviceHost runner, ArtifactSession artifacts)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(artifacts);

        var data = await _dependencies.CommandDispatcher.ExecuteAsync(options.Command!, options, adbExecutable, runner).ConfigureAwait(false);
        WriteSuccess(options.Command!, started, data, artifacts.ToData());
        return 0;
    }

    public void WriteUsageError(string? command, DateTimeOffset started, ArtifactData artifacts, UsageException exception) =>
        WriteEnvelope(new CommandEnvelope(false, command, started, _dependencies.TimeProvider.GetUtcNow(), null, artifacts, ErrorInfo.From(exception, "usage_error")));

    public void WriteFailure(string? command, DateTimeOffset started, object? data, ArtifactData artifacts, Exception exception, string category) =>
        WriteEnvelope(new CommandEnvelope(false, command, started, _dependencies.TimeProvider.GetUtcNow(), data, artifacts, ErrorInfo.From(exception, category)));

    private void WriteSuccess(string command, DateTimeOffset started, object data, ArtifactData artifacts) =>
        WriteEnvelope(new CommandEnvelope(true, command, started, _dependencies.TimeProvider.GetUtcNow(), data, artifacts, null));

    private void WriteEnvelope(CommandEnvelope envelope) =>
        _dependencies.Console.WriteLine(JsonSerializer.Serialize(envelope, JsonOptions));
}

internal sealed class AppCommandHostDependencies
{
    public required IConsoleIo Console { get; init; }

    public required TimeProvider TimeProvider { get; init; }

    public required ViewProfileCoordinator ProfileCoordinator { get; init; }

    public required AppCommandDispatcher CommandDispatcher { get; init; }

    public required IViewDoctorFactory ViewDoctorFactory { get; init; }
}