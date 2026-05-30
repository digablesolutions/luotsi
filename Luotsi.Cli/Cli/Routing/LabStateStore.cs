using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Infrastructure.Contracts;

namespace Luotsi.Cli.Cli.Routing;

internal interface ILabStateStore
{
    string GetCollectionRoot(string collectionName);
}

internal static class LabStateStoreFactory
{
    internal const string SharedRootEnvironmentVariable = "LUOTSI_LAB_STATE_ROOT";

    public static ILabStateStore Create(IFileSystem fileSystem, IEnvironmentVariables? environment)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        var sharedRoot = environment?.GetEnvironmentVariable(SharedRootEnvironmentVariable);
        return !string.IsNullOrWhiteSpace(sharedRoot)
            ? new SharedRootLabStateStore(sharedRoot.Trim())
            : new WorkspaceLabStateStore(ArtifactWorkspacePaths.ResolveDefaultWorkspaceRoot(fileSystem, environment));
    }

    internal static string NormalizeCollectionName(string collectionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionName);

        var normalized = collectionName.Trim();
        foreach (var ch in normalized)
        {
            if (!char.IsLetterOrDigit(ch) && ch is not '-' and not '_')
            {
                throw new ArgumentException("Lab state collection names must use only letters, digits, hyphens, or underscores.", nameof(collectionName));
            }
        }

        return normalized;
    }
}

internal sealed class WorkspaceLabStateStore(string workspaceRoot) : ILabStateStore
{
    private readonly string _workspaceRoot = string.IsNullOrWhiteSpace(workspaceRoot)
        ? throw new ArgumentException("Workspace root must not be empty.", nameof(workspaceRoot))
        : workspaceRoot;

    public string GetCollectionRoot(string collectionName)
    {
        return Path.Join(_workspaceRoot, "lab", LabStateStoreFactory.NormalizeCollectionName(collectionName));
    }
}

internal sealed class SharedRootLabStateStore(string sharedRoot) : ILabStateStore
{
    private readonly string _sharedRoot = string.IsNullOrWhiteSpace(sharedRoot)
        ? throw new ArgumentException("Shared root must not be empty.", nameof(sharedRoot))
        : sharedRoot;

    public string GetCollectionRoot(string collectionName)
    {
        return Path.Join(_sharedRoot, LabStateStoreFactory.NormalizeCollectionName(collectionName));
    }
}
