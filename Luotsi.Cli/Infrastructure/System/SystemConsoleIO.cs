using Luotsi.Cli.Infrastructure.Contracts;

namespace Luotsi.Cli.Infrastructure.System;

public sealed class SystemConsoleIo : IConsoleIo
{
    public bool SupportsAnsiStyling => SupportsAnsiStylingCore();

    public void WriteLine(string value) => Console.Out.WriteLine(value);

    public void WriteErrorLine(string value) => Console.Error.WriteLine(value);

    public string? ReadLine() => Console.In.ReadLine();

    private static bool SupportsAnsiStylingCore()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR")))
        {
            return false;
        }

        var forceColor = Environment.GetEnvironmentVariable("CLICOLOR_FORCE");
        if (!string.IsNullOrEmpty(forceColor) && forceColor != "0")
        {
            return true;
        }

        if (Console.IsOutputRedirected || Console.IsErrorRedirected)
        {
            return false;
        }

        return !string.Equals(Environment.GetEnvironmentVariable("CLICOLOR"), "0", StringComparison.Ordinal);
    }
}
