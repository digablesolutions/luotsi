namespace VisitLab.Cli;

public sealed class SystemConsoleIO : IConsoleIO
{
    public void WriteLine(string value) => Console.Out.WriteLine(value);

    public void WriteErrorLine(string value) => Console.Error.WriteLine(value);

    public string? ReadLine() => Console.In.ReadLine();
}