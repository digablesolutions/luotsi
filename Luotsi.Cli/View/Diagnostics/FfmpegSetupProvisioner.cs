using System.ComponentModel;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Infrastructure.Resilience;
using Luotsi.Cli.Models;
using Luotsi.Cli.View.Contracts;
using Polly;
using Polly.Registry;

namespace Luotsi.Cli.View.Diagnostics;

internal sealed class FfmpegSetupProvisioner(
    IEnvironmentVariables environment,
    IFileSystem fileSystem,
    IProcessRunner processRunner,
    ResiliencePipelineProvider<string>? resiliencePipelines = null)
{
    private static readonly string[] WindowsPowerShellHosts = ["pwsh", "powershell"];
    private static readonly string[] PosixPowerShellHosts = ["pwsh"];
    private const string ScriptRelativePath = "ffmpeg/download-ffmpeg.ps1";

    private readonly ViewHostPathResolver _pathResolver = new(environment ?? throw new ArgumentNullException(nameof(environment)));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly IProcessRunner _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    private readonly ResiliencePipeline _setupDownloadPipeline =
        resiliencePipelines?.GetPipeline(LuotsiResiliencePipelines.SetupDownloadPipelineName) ??
        LuotsiResiliencePipelines.CreateSetupDownloadPipeline();

    public async Task<bool> StageAsync(Action<ViewSetupStep> reportStep, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reportStep);

        var scriptPath = _pathResolver.GetRepositoryRelativeFileCandidates(ScriptRelativePath).FirstOrDefault(_fileSystem.FileExists);
        if (string.IsNullOrWhiteSpace(scriptPath))
        {
            reportStep(new ViewSetupStep(
                "ffmpeg_stage",
                ViewStartupPhaseStatus.Failed,
                "FFmpeg staging script was not found.",
                ScriptRelativePath,
                "Run this command from the repository root or stage FFmpeg manually with ffmpeg/download-ffmpeg.ps1."));
            return false;
        }

        var hosts = GetPowerShellHosts();
        reportStep(new ViewSetupStep("ffmpeg_stage", ViewStartupPhaseStatus.Started, "Staging FFmpeg native libraries.", scriptPath));
        for (var index = 0; index < hosts.Count; index++)
        {
            var host = hosts[index];
            try
            {
                var attempts = 0;
                var result = await _setupDownloadPipeline.ExecuteAsync(
                    async token =>
                    {
                        attempts++;
                        var processResult = await _processRunner.RunAsync(host, BuildArguments(scriptPath), token).ConfigureAwait(false);
                        if (processResult.ExitCode != 0 && IsTransientSetupFailure(processResult))
                        {
                            throw new SetupDownloadTransientException(PreferError(processResult));
                        }

                        return processResult;
                    },
                    cancellationToken).ConfigureAwait(false);
                if (result.ExitCode == 0)
                {
                    var detail = string.IsNullOrWhiteSpace(result.Stdout) ? result.Stderr.Trim() : result.Stdout.Trim();
                    var suffix = attempts > 1 ? $" after {attempts} attempts" : string.Empty;
                    reportStep(new ViewSetupStep("ffmpeg_stage", ViewStartupPhaseStatus.Succeeded, $"FFmpeg native libraries are ready{suffix}.", string.IsNullOrWhiteSpace(detail) ? null : detail));
                    return true;
                }

                reportStep(new ViewSetupStep(
                    "ffmpeg_stage",
                    ViewStartupPhaseStatus.Failed,
                    "FFmpeg staging failed.",
                    PreferError(result),
                    "Review the FFmpeg staging script output, then rerun doctor --fix or stage FFmpeg manually with ffmpeg/download-ffmpeg.ps1."));
                return false;
            }
            catch (SetupDownloadTransientException ex)
            {
                reportStep(new ViewSetupStep(
                    "ffmpeg_stage",
                    ViewStartupPhaseStatus.Failed,
                    "FFmpeg staging failed after retrying transient download failures.",
                    ex.Message,
                    "Check network access to the FFmpeg release host, then rerun doctor --fix or stage FFmpeg manually with ffmpeg/download-ffmpeg.ps1."));
                return false;
            }
            catch (Exception ex) when (IsExpectedProcessException(ex))
            {
                if (index < hosts.Count - 1)
                {
                    continue;
                }

                reportStep(new ViewSetupStep(
                    "ffmpeg_stage",
                    ViewStartupPhaseStatus.Failed,
                    "PowerShell is required to stage FFmpeg native libraries.",
                    ex.Message,
                    "Install PowerShell 7 (`pwsh`) or run ffmpeg/download-ffmpeg.ps1 manually in a shell that can execute PowerShell scripts."));
                return false;
            }
        }

        return false;
    }

    private static IReadOnlyList<string> GetPowerShellHosts() => OperatingSystem.IsWindows() ? WindowsPowerShellHosts : PosixPowerShellHosts;

    private static IReadOnlyList<string> BuildArguments(string scriptPath) =>
        ["-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", scriptPath];

    private static bool IsExpectedProcessException(Exception exception) =>
        exception is InvalidOperationException or IOException or UnauthorizedAccessException or Win32Exception;

    private static bool IsTransientSetupFailure(ProcessResult result)
    {
        var output = $"{result.Stderr}\n{result.Stdout}";
        return output.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("connection", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("temporarily unavailable", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("could not resolve", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("name resolution", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("remote name could not be resolved", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("operation has timed out", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("503", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("502", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("429", StringComparison.OrdinalIgnoreCase);
    }

    private static string PreferError(ProcessResult result) =>
        string.IsNullOrWhiteSpace(result.Stderr) ? result.Stdout.Trim() : result.Stderr.Trim();
}
