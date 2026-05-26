using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Cli.Envelope;
using Luotsi.Cli.Errors;

namespace Luotsi.Cli.Cli.Routing;

internal sealed class ArtifactCommandHost(
    ArtifactCommandService artifactCommandService,
    AppCommandEnvelopeWriter envelopeWriter)
{
    private readonly ArtifactCommandService _artifactCommandService = artifactCommandService ?? throw new ArgumentNullException(nameof(artifactCommandService));
    private readonly AppCommandEnvelopeWriter _envelopeWriter = envelopeWriter ?? throw new ArgumentNullException(nameof(envelopeWriter));

    public async Task<int> RunAsync(CliOptions options, DateTimeOffset started, ArtifactSession artifacts)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(artifacts);

        var subcommand = options.Arguments.Count > 0 ? options.Arguments[0] : null;
        object data = subcommand?.ToLowerInvariant() switch
        {
            "open" => await OpenAsync(options).ConfigureAwait(false),
            "pack" => await PackAsync(options).ConfigureAwait(false),
            _ => throw new UsageException("artifacts command must be one of: open, pack.")
        };

        _envelopeWriter.WriteSuccess(options.Command!, started, data, artifacts.ToData(), AppCommandConsoleOutputModeResolver.Resolve(options));
        return 0;
    }

    private Task<ArtifactOpenResult> OpenAsync(CliOptions options)
    {
        var target = RequireTarget(options, "open");
        return _artifactCommandService.OpenAsync(target, options.Get("artifacts"), options.HasFlag("dry-run"));
    }

    private Task<ArtifactPackResult> PackAsync(CliOptions options)
    {
        var target = RequireTarget(options, "pack");
        return _artifactCommandService.PackAsync(target, options.Get("artifacts"), options.Get("output"), options.HasFlag("force"));
    }

    private static string RequireTarget(CliOptions options, string subcommand)
    {
        if (options.Arguments.Count < 2 || string.IsNullOrWhiteSpace(options.Arguments[1]))
        {
            throw new UsageException($"artifacts {subcommand} requires <artifact-root-or-run-id>.");
        }

        return options.Arguments[1];
    }
}
