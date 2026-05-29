using System.Text.Json;
using Luotsi.Cli.Infrastructure.Contracts;

namespace Luotsi.Cli.Cli.Envelope;

internal sealed class AppCommandJsonWriter(IConsoleIo console)
{
    private readonly IConsoleIo _console = console ?? throw new ArgumentNullException(nameof(console));

    public void Write(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _console.WriteLine(JsonSerializer.Serialize(value, AppCommandJson.Options));
    }

    public void WriteLines(IEnumerable<object> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        foreach (var value in values)
        {
            Write(value);
        }
    }
}