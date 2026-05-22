using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli.Routing;

internal static class LabDoctorProbes
{
    public static async Task<IReadOnlyList<LabDoctorProbe>> RunAsync(IDeviceHost runner)
    {
        if (runner is not IAdbCommandHost adb)
        {
            return [];
        }

        var probes = new List<LabDoctorProbe>();
        foreach (var probe in new (string Name, Func<Task<AdbDiagnosticResult>> Run)[]
        {
            ("server-status", adb.GetAdbServerStatusAsync),
            ("version", adb.GetAdbVersionAsync),
            ("features", adb.GetAdbFeaturesAsync),
            ("mdns-check", adb.CheckAdbMdnsAsync)
        })
        {
            probes.Add(await RunProbeAsync(probe.Name, probe.Run).ConfigureAwait(false));
        }

        return probes;
    }

    private static async Task<LabDoctorProbe> RunProbeAsync(string name, Func<Task<AdbDiagnosticResult>> runAsync)
    {
        try
        {
            var result = await runAsync().ConfigureAwait(false);
            return new LabDoctorProbe(name, result.Command.Succeeded, result.Command.ExitCode, result.Command.Invocation);
        }
        catch (Exception ex)
        {
            return new LabDoctorProbe(name, false, -1, ex.Message);
        }
    }
}
