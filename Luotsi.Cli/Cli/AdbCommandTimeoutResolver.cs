using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;

namespace Luotsi.Cli.Cli;

internal static class AdbCommandTimeoutResolver
{
    public static TimeSpan? Resolve(CliOptions options, IEnvironmentVariables environment)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);

        var rawValue = options.Get("adb-timeout-sec") ??
            environment.GetEnvironmentVariable(CliDefaults.AdbCommandTimeoutEnvironmentVariable) ??
            CliDefaults.DefaultAdbCommandTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);

        if (!int.TryParse(rawValue, out var timeoutSec) || timeoutSec < 0)
        {
            throw new UsageException("Option --adb-timeout-sec must be a non-negative integer.");
        }

        return timeoutSec == 0 ? null : TimeSpan.FromSeconds(timeoutSec);
    }
}