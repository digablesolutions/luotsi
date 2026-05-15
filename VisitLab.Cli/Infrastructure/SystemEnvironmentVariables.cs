namespace VisitLab.Cli;

public sealed class SystemEnvironmentVariables : IEnvironmentVariables
{
    public string? GetEnvironmentVariable(string variable) => Environment.GetEnvironmentVariable(variable);
}