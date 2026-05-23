using System.Text.Json;
using Luotsi.Cli.Cli.Routing;
using Xunit;

namespace Luotsi.Cli.Tests;

public sealed class LabLeaseStoreTests
{
    [Fact]
    public async Task ListAsync_Deletes_Corrupt_Lease_File()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-23T06:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var leaseRoot = Path.Join(fileSystem.GetTempPath(), "luotsi", "lab-leases");
        var leasePath = Path.Join(leaseRoot, "usb-1.json");
        fileSystem.CreateDirectory(leaseRoot);
        fileSystem.AddFile(leasePath, "{ not-json");
        var store = new LabLeaseStore(fileSystem, timeProvider);

        var result = await store.ListAsync();

        Assert.Equal(0, result.Count);
        Assert.False(fileSystem.FileExists(leasePath));
        Assert.Contains(leasePath, fileSystem.DeletedFiles);
    }
}