namespace VisitLab.Cli.Infrastructure;

public sealed class GuidUniqueIdGenerator : IUniqueIdGenerator
{
    public string NewId() => Guid.NewGuid().ToString("N");
}