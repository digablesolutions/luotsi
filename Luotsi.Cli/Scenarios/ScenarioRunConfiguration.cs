using Luotsi.Cli.Cli;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Scenarios;

internal enum ScenarioArtifactAttachmentPolicy
{
    Never,
    OnFailure,
    Always
}

internal enum ScenarioProgressMode
{
    Plain,
    Line,
    Quiet,
    Jsonl
}

internal sealed record ScenarioRunConfiguration(
    string? EventsJsonlPath,
    string? JsonReportPath,
    string? JUnitReportPath,
    ScenarioFailureArtifactCapturePolicy FailureArtifactCapturePolicy,
    ScenarioArtifactAttachmentPolicy ArtifactAttachmentPolicy,
    bool ValidateOnly,
    bool RequireDeviceReady,
    int DeviceWaitTimeoutSec,
    string? DeviceReadinessPackage,
    ScenarioProgressMode ProgressMode = ScenarioProgressMode.Plain,
    LabLeaseResult? LabLease = null)
{
    public static ScenarioRunConfiguration Create(CliOptions options, IEnvironmentVariables? environment = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new ScenarioRunConfiguration(
            NormalizePath(options.Get("events-jsonl")),
            NormalizePath(options.Get("report-json")),
            NormalizePath(options.Get("report-junit")),
            ParseFailureArtifactCapturePolicy(options.Get("capture-on")),
            ParseArtifactAttachmentPolicy(options.Get("attach-artifacts")),
            options.HasFlag("validate-only"),
            !options.HasFlag("no-require-device-ready"),
            options.Int("device-ready-timeout-sec", CliDefaults.DefaultTimeoutSeconds),
            NormalizePackage(options.Get("package")),
            ParseProgressMode(options.Get("progress"), options.HasFlag("quiet"), environment));
    }

    private static string? NormalizePath(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static string? NormalizePackage(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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

    private static ScenarioProgressMode ParseProgressMode(string? value, bool quiet, IEnvironmentVariables? environment)
    {
        if (quiet && !string.IsNullOrWhiteSpace(value) && !string.Equals(value.Trim(), "quiet", StringComparison.OrdinalIgnoreCase))
        {
            throw new UsageException("Use either --quiet or --progress plain/line/jsonl, not both.");
        }

        if (quiet)
        {
            return ScenarioProgressMode.Quiet;
        }

        if (string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), "auto", StringComparison.OrdinalIgnoreCase))
        {
            return IsCi(environment) ? ScenarioProgressMode.Line : ScenarioProgressMode.Plain;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "plain" => ScenarioProgressMode.Plain,
            "line" => ScenarioProgressMode.Line,
            "quiet" => ScenarioProgressMode.Quiet,
            "jsonl" => ScenarioProgressMode.Jsonl,
            _ => throw new UsageException("--progress must be one of: auto, line, plain, quiet, jsonl.")
        };
    }

    private static bool IsCi(IEnvironmentVariables? environment)
    {
        if (environment is null)
        {
            return false;
        }

        var ci = environment.GetEnvironmentVariable("CI");
        return !string.IsNullOrWhiteSpace(ci) &&
            !string.Equals(ci, "0", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(ci, "false", StringComparison.OrdinalIgnoreCase);
    }
}
