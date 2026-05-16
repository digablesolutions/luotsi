namespace Luotsi.Cli.Errors;

/// <summary>
/// Usage error.
/// </summary>
public sealed class UsageException(string message) : Exception(message);