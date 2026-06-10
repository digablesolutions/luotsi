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
            "list" => await ListAsync(options).ConfigureAwait(false),
            "info" => await InfoAsync(options).ConfigureAwait(false),
            "open" => await OpenAsync(options).ConfigureAwait(false),
            "pack" => await PackAsync(options).ConfigureAwait(false),
            "verify" => await VerifyAsync(options).ConfigureAwait(false),
            "unpack" => await UnpackAsync(options).ConfigureAwait(false),
            "intake" => await IntakeAsync(options).ConfigureAwait(false),
            _ => throw new UsageException("artifacts command must be one of: list, info, open, pack, verify, unpack, intake.")
        };

        _envelopeWriter.WriteSuccess(options.Command!, started, data, artifacts.ToData(), AppCommandConsoleOutputModeResolver.Resolve(options));
        return data is ArtifactVerifyResult { Status: "blocked" } ? 1 : 0;
    }

    private Task<ArtifactListResult> ListAsync(CliOptions options) =>
        _artifactCommandService.ListAsync(options.Get("artifacts"), options.Int("limit", 20));

    private Task<object> InfoAsync(CliOptions options)
    {
        var target = ResolveTarget(options, "info", allowLast: true);
        return _artifactCommandService.InfoAsync(target, options.Get("artifacts"), options.HasFlag("last"));
    }

    private Task<ArtifactOpenResult> OpenAsync(CliOptions options)
    {
        var target = ResolveTarget(options, "open", allowLast: true);
        return _artifactCommandService.OpenAsync(target, options.Get("artifacts"), options.HasFlag("dry-run"), options.HasFlag("last"));
    }

    private Task<ArtifactPackResult> PackAsync(CliOptions options)
    {
        var target = RequireTarget(options, "pack", "<artifact-root-or-run-id>");
        return _artifactCommandService.PackAsync(target, options.Get("artifacts"), options.Get("output"), options.HasFlag("force"), options.HasFlag("dry-run"), options.Get("redact"));
    }

    private Task<ArtifactVerifyResult> VerifyAsync(CliOptions options)
    {
        var target = RequireTarget(options, "verify", "<artifact.zip>");
        return _artifactCommandService.VerifyAsync(target, options.Get("output"), options.HasFlag("require-lab-safe"), options.Get("sha256"));
    }

    private Task<ArtifactUnpackResult> UnpackAsync(CliOptions options)
    {
        var target = RequireTarget(options, "unpack", "<artifact.zip>");
        return _artifactCommandService.UnpackAsync(target, options.Get("output"), options.HasFlag("force"), options.HasFlag("dry-run"), options.HasFlag("require-lab-safe"), options.Get("sha256"));
    }

    private Task<ArtifactIntakeResult> IntakeAsync(CliOptions options)
    {
        var target = RequireTarget(options, "intake", "<artifact.zip>");
        return _artifactCommandService.IntakeAsync(target, options.Get("output"), options.HasFlag("force"), options.HasFlag("dry-run"), options.HasFlag("require-lab-safe"), options.HasFlag("open"), options.HasFlag("write-json"), options.HasFlag("write-readme"), options.Get("sha256"));
    }

    private static string RequireTarget(CliOptions options, string subcommand, string argumentName)
    {
        var target = ResolveTarget(options, subcommand);
        if (string.IsNullOrWhiteSpace(target))
        {
            throw new UsageException($"artifacts {subcommand} requires {argumentName}.");
        }

        return target;
    }

    private static string? ResolveTarget(CliOptions options, string subcommand, bool allowLast = false)
    {
        var hasTarget = options.Arguments.Count >= 2 && !string.IsNullOrWhiteSpace(options.Arguments[1]);
        var useLast = allowLast && options.HasFlag("last");
        if (hasTarget && useLast)
        {
            throw new UsageException($"Use either <artifact-root-or-run-id> or --last with artifacts {subcommand}, not both.");
        }

        if (useLast)
        {
            return null;
        }

        return hasTarget ? options.Arguments[1] : null;
    }
}
