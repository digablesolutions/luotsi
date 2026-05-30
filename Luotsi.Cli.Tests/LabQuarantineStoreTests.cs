using Luotsi.Cli.Cli.Routing;
using Xunit;

namespace Luotsi.Cli.Tests;

public sealed class LabQuarantineStoreTests
{
    [Fact]
    public async Task ListAsync_Deletes_Corrupt_Quarantine_File()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-30T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var quarantineRoot = Path.Join(fileSystem.GetTempPath(), "luotsi", "lab", "quarantines");
        var quarantinePath = Path.Join(quarantineRoot, "usb-1.json");
        fileSystem.CreateDirectory(quarantineRoot);
        fileSystem.AddFile(quarantinePath, "{ bad-json");
        var store = new LabQuarantineStore(fileSystem, timeProvider);

        var result = await store.ListAsync();

        Assert.Equal(0, result.Count);
        Assert.False(fileSystem.FileExists(quarantinePath));
        Assert.Contains(quarantinePath, fileSystem.DeletedFiles);
    }

    [Fact]
    public async Task QuarantineAsync_Uses_Shared_Lab_State_Root_When_Configured()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-30T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var environment = new FakeEnvironmentVariables(new Dictionary<string, string>
        {
            [LabStateStoreFactory.SharedRootEnvironmentVariable] = @"C:\lab-state"
        });
        var store = new LabQuarantineStore(fileSystem, timeProvider, environment);

        var quarantine = await store.QuarantineAsync("usb-1", "flaky touchscreen", "lab-admin");

        Assert.Equal(Path.Join(@"C:\lab-state", "quarantines", "usb-1.json"), quarantine.QuarantineFile);
        Assert.True(fileSystem.FileExists(quarantine.QuarantineFile));
    }
}
