using Luotsi.Cli.Infrastructure.Contracts;

namespace Luotsi.Cli.Infrastructure.System;

public sealed class SystemConsoleIo : IConsoleIo
{
    public void WriteLine(string value) => Console.Out.WriteLine(value);

    public void WriteErrorLine(string value) => Console.Error.WriteLine(value);

    public string? ReadLine() => Console.In.ReadLine();
}