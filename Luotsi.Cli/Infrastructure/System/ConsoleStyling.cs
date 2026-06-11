using Luotsi.Cli.Infrastructure.Contracts;

namespace Luotsi.Cli.Infrastructure.System;

internal static class ConsoleStyling
{
    private const string Reset = "\u001b[0m";

    public static string Success(IConsoleIo console, string value) => Style(console, "32;1", value);

    public static string Warning(IConsoleIo console, string value) => Style(console, "33;1", value);

    public static string Failure(IConsoleIo console, string value) => Style(console, "31;1", value);

    public static string Muted(IConsoleIo console, string value) => Style(console, "90", value);

    public static string Accent(IConsoleIo console, string value) => Style(console, "36;1", value);

    private static string Style(IConsoleIo console, string code, string value)
        => console.SupportsAnsiStyling ? $"\u001b[{code}m{value}{Reset}" : value;
}
