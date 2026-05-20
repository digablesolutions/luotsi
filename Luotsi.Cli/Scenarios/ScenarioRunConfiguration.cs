using Luotsi.Cli.Cli;
using Luotsi.Cli.Errors;

namespace Luotsi.Cli.Scenarios;

internal enum ScenarioArtifactAttachmentPolicy
{
    Never,
    OnFailure,
    Always
}

internal sealed record ScenarioRunConfiguration(
    string? EventsJsonlPath,
    string? JsonReportPath,
    string? JUnitReportPath,
    ScenarioFailureArtifactCapturePolicy FailureArtifactCapturePolicy,
    ScenarioArtifactAttachmentPolicy ArtifactAttachmentPolicy,
    bool ValidateOnly)
{
    public static ScenarioRunConfiguration Create(CliOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new ScenarioRunConfiguration(
            NormalizePath(options.Get("events-jsonl")),
            NormalizePath(options.Get("report-json")),
            NormalizePath(options.Get("report-junit")),
            ParseFailureArtifactCapturePolicy(options.Get("capture-on")),
            ParseArtifactAttachmentPolicy(options.Get("attach-artifacts")),
            options.HasFlag("validate-only"));
    }

    private static string? NormalizePath(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static ScenarioFailureArtifactCapturePolicy ParseFailureArtifactCapturePolicy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ScenarioFailureArtifactCapturePolicy.Failure;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "failure" or "on-failure" or "onfailure" => ScenarioFailureArtifactCapturePolicy.Failure,
            "never" => ScenarioFailureArtifactCapturePolicy.Never,
            _ => throw new UsageException("--capture-on must be one of: failure, never.")
        };
    }

    private static ScenarioArtifactAttachmentPolicy ParseArtifactAttachmentPolicy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ScenarioArtifactAttachmentPolicy.OnFailure;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "never" => ScenarioArtifactAttachmentPolicy.Never,
            "on-failure" or "onfailure" => ScenarioArtifactAttachmentPolicy.OnFailure,
            "always" => ScenarioArtifactAttachmentPolicy.Always,
            _ => throw new UsageException("--attach-artifacts must be one of: never, on-failure, always.")
        };
    }
}
