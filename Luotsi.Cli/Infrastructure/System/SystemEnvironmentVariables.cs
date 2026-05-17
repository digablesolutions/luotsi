using Luotsi.Cli.Infrastructure.Contracts;

namespace Luotsi.Cli.Infrastructure.System;

public sealed class SystemEnvironmentVariables : IEnvironmentVariables
{
    public string? GetEnvironmentVariable(string variable) => Environment.GetEnvironmentVariable(variable);
}