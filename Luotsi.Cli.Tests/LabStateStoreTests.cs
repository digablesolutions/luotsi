using Luotsi.Cli.Cli.Routing;
using Xunit;

namespace Luotsi.Cli.Tests;

public sealed class LabStateStoreTests
{
    [Fact]
    public void Create_Without_Shared_Root_Uses_Workspace_Store()
    {
        var fileSystem = new FakeFileSystem();

        var store = LabStateStoreFactory.Create(fileSystem, environment: null);

        var collectionRoot = Assert.IsType<WorkspaceLabStateStore>(store).GetCollectionRoot("leases");
        Assert.Equal(Path.Join(fileSystem.GetTempPath(), "luotsi", "lab", "leases"), collectionRoot);
    }

    [Fact]
    public void Create_With_Whitespace_Shared_Root_Falls_Back_To_Workspace_Store()
    {
        var fileSystem = new FakeFileSystem();
        var environment = new FakeEnvironmentVariables(new Dictionary<string, string>
        {
            [LabStateStoreFactory.SharedRootEnvironmentVariable] = "   "
        });

        var store = LabStateStoreFactory.Create(fileSystem, environment);

        Assert.IsType<WorkspaceLabStateStore>(store);
    }

    [Fact]
    public void Create_With_Shared_Root_Trims_And_Uses_Shared_Store()
    {
        var fileSystem = new FakeFileSystem();
        var environment = new FakeEnvironmentVariables(new Dictionary<string, string>
        {
            [LabStateStoreFactory.SharedRootEnvironmentVariable] = "  C:\\lab-state  "
        });

        var store = LabStateStoreFactory.Create(fileSystem, environment);

        var collectionRoot = Assert.IsType<SharedRootLabStateStore>(store).GetCollectionRoot("device-health");
        Assert.Equal(Path.Join(@"C:\lab-state", "device-health"), collectionRoot);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("../leases")]
    [InlineData("..\\leases")]
    [InlineData("lease/root")]
    public void GetCollectionRoot_Rejects_Invalid_Collection_Names(string collectionName)
    {
        var store = new WorkspaceLabStateStore(@"C:\workspace");

        var error = Assert.Throws<ArgumentException>(() => store.GetCollectionRoot(collectionName));

        Assert.Equal("collectionName", error.ParamName);
    }
}
