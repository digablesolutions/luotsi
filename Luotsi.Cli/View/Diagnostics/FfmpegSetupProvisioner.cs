using System.ComponentModel;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;
using Luotsi.Cli.View.Contracts;

namespace Luotsi.Cli.View.Diagnostics;

internal sealed class FfmpegSetupProvisioner(
    IEnvironmentVariables environment,
    IFileSystem fileSystem,
    IProcessRunner processRunner)
{
    private static readonly string[] WindowsPowerShellHosts = ["pwsh", "powershell"];
    private static readonly string[] PosixPowerShellHosts = ["pwsh"];
    private const string ScriptRelativePath = "ffmpeg/download-ffmpeg.ps1";

    private readonly ViewHostPathResolver _pathResolver = new(environment ?? throw new ArgumentNullException(nameof(environment)));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly IProcessRunner _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));

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
                var result = await _processRunner.RunAsync(host, BuildArguments(scriptPath), cancellationToken).ConfigureAwait(false);
                if (result.ExitCode == 0)
                {
                    var detail = string.IsNullOrWhiteSpace(result.Stdout) ? result.Stderr.Trim() : result.Stdout.Trim();
                    reportStep(new ViewSetupStep("ffmpeg_stage", ViewStartupPhaseStatus.Succeeded, "FFmpeg native libraries are ready.", string.IsNullOrWhiteSpace(detail) ? null : detail));
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

    private static string PreferError(ProcessResult result) =>
        string.IsNullOrWhiteSpace(result.Stderr) ? result.Stdout.Trim() : result.Stderr.Trim();
}