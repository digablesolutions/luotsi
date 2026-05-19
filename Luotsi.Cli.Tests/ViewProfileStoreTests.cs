using Luotsi.Cli.Cli;
using Luotsi.Cli.Cli.View;
using Xunit;

namespace Luotsi.Cli.Tests;

public sealed class ViewProfileStoreTests
{
    [Fact]
    public async Task SaveLoadAndListAsync_Use_FileSystem_Seam_For_Profile_Json()
    {
        var fileSystem = new FakeFileSystem();
        var environment = new FakeEnvironmentVariables(new Dictionary<string, string>
        {
            ["LUOTSI_PROFILE_ROOT"] = "/profiles"
        });
        var store = new JsonViewProfileStore(fileSystem, environment);
        var profile = new ViewProfile(Device: "emulator-5554", Adb: "adb");

        await store.SaveAsync("pixel", profile);
        var loaded = await store.LoadAsync("pixel");
        var profiles = await store.ListAsync();
        var savedFiles = fileSystem.GetFiles("/profiles", "*.json", SearchOption.TopDirectoryOnly);

        Assert.NotNull(loaded);
        Assert.Equal("emulator-5554", loaded.Device);
        Assert.Equal("adb", loaded.Adb);
        Assert.Equal(["pixel"], profiles);
        Assert.Single(savedFiles);
    }
}