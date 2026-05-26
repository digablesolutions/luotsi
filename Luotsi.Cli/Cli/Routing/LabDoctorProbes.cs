using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Infrastructure.Resilience;
using Luotsi.Cli.Models;
using Polly;

namespace Luotsi.Cli.Cli.Routing;

internal static class LabDoctorProbes
{
    public static async Task<IReadOnlyList<LabDoctorProbe>> RunAsync(IDeviceHost runner, ResiliencePipeline? labProbePipeline = null)
    {
        if (runner is not IAdbCommandHost adb)
        {
            return [];
        }

        var pipeline = labProbePipeline ?? LuotsiResiliencePipelines.CreateLabProbePipeline();
        var probes = new List<LabDoctorProbe>();
        foreach (var probe in new (string Name, Func<Task<AdbDiagnosticResult>> Run)[]
        {
            ("server-status", adb.GetAdbServerStatusAsync),
            ("version", adb.GetAdbVersionAsync),
            ("features", adb.GetAdbFeaturesAsync),
            ("mdns-check", adb.CheckAdbMdnsAsync)
        })
        {
            probes.Add(await RunProbeAsync(probe.Name, probe.Run, pipeline).ConfigureAwait(false));
        }

        return probes;
    }

    private static async Task<LabDoctorProbe> RunProbeAsync(string name, Func<Task<AdbDiagnosticResult>> runAsync, ResiliencePipeline labProbePipeline)
    {
        var attempts = 0;
        try
        {
            var result = await labProbePipeline.ExecuteAsync(
                async _ =>
                {
                    attempts++;
                    var diagnostic = await runAsync().ConfigureAwait(false);
                    if (!diagnostic.Command.Succeeded && IsTransientProbeFailure(diagnostic.Command))
                    {
                        throw new LabProbeTransientException(
                            $"{name} exited {diagnostic.Command.ExitCode}: {PreferError(diagnostic.Command)}",
                            diagnostic.Command.ExitCode,
                            diagnostic.Command.Invocation);
                    }

                    return diagnostic;
                }).ConfigureAwait(false);
            return new LabDoctorProbe(name, result.Command.Succeeded, result.Command.ExitCode, result.Command.Invocation, attempts, Math.Max(0, attempts - 1));
        }
        catch (LabProbeTransientException ex)
        {
            return new LabDoctorProbe(name, false, ex.ExitCode, ex.Invocation, Math.Max(1, attempts), Math.Max(0, attempts - 1));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new LabDoctorProbe(name, false, -1, ex.Message, Math.Max(1, attempts), Math.Max(0, attempts - 1));
        }
    }

    private static bool IsTransientProbeFailure(AdbCommandOutput command)
    {
        var output = $"{command.Stderr}\n{command.Stdout}";
        return output.Contains("protocol fault", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("no status", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("device still connecting", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("device offline", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("device not found", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("no devices/emulators found", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("transport is not ready", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("temporarily unavailable", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("timeout", StringComparison.OrdinalIgnoreCase);
    }

    private static string PreferError(AdbCommandOutput command) =>
        string.IsNullOrWhiteSpace(command.Stderr) ? command.Stdout.Trim() : command.Stderr.Trim();
}
