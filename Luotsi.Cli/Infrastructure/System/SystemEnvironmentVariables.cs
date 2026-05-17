namespace Luotsi.Cli.Infrastructure;

public sealed class SystemEnvironmentVariables : IEnvironmentVariables
{
    public string? GetEnvironmentVariable(string variable) => Environment.GetEnvironmentVariable(variable);
}