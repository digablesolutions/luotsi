namespace VisitLab.Cli;

/// <summary>
/// Usage error.
/// </summary>
public sealed class UsageException(string message) : Exception(message);