using Luotsi.Cli.Infrastructure.Contracts;

namespace Luotsi.Cli.Infrastructure.Ids;

public sealed class GuidUniqueIdGenerator : IUniqueIdGenerator
{
    public string NewId() => Guid.NewGuid().ToString("N");
}