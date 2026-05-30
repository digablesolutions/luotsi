using System.Text.Json;
using Luotsi.Cli.Cli.Routing;
using Luotsi.Cli.Errors;
using Xunit;

namespace Luotsi.Cli.Tests;

public sealed class LabLeaseStoreTests
{
    [Fact]
    public async Task ListAsync_Deletes_Corrupt_Lease_File()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-23T06:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var leaseRoot = Path.Join(fileSystem.GetTempPath(), "luotsi", "lab", "leases");
        var leasePath = Path.Join(leaseRoot, "usb-1.json");
        fileSystem.CreateDirectory(leaseRoot);
        fileSystem.AddFile(leasePath, "{ not-json");
        var store = new LabLeaseStore(fileSystem, timeProvider);

        var result = await store.ListAsync();

        Assert.Equal(0, result.Count);
        Assert.False(fileSystem.FileExists(leasePath));
        Assert.Contains(leasePath, fileSystem.DeletedFiles);
    }

    [Fact]
    public async Task ClaimAsync_Uses_Workspace_Root_When_User_Local_Path_Is_Available()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-23T06:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var environment = OperatingSystem.IsWindows()
            ? new FakeEnvironmentVariables(new Dictionary<string, string> { ["LOCALAPPDATA"] = @"C:\Users\agent\AppData\Local" })
            : new FakeEnvironmentVariables(new Dictionary<string, string> { ["HOME"] = "/home/agent" });
        var store = new LabLeaseStore(fileSystem, timeProvider, environment);

        var lease = await store.ClaimAsync("usb-1", "ci-job-1", 60);

        var expectedRoot = OperatingSystem.IsWindows()
            ? Path.Join(@"C:\Users\agent\AppData\Local", "Luotsi", "lab", "leases")
            : Path.Join("/home/agent", ".local", "share", "luotsi", "lab", "leases");
        Assert.Equal(Path.Join(expectedRoot, "usb-1.json"), lease.LeaseFile);
        Assert.True(fileSystem.FileExists(lease.LeaseFile));
    }

    [Fact]
    public async Task ClaimAsync_Blocks_Direct_Claims_When_Queue_Is_Already_Active()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-23T06:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var store = new LabLeaseStore(fileSystem, timeProvider);

        await store.EnqueueAsync("usb-1", "ci-job-1", 60);
        var error = await Assert.ThrowsAsync<UsageException>(() => store.ClaimAsync("usb-1", "ci-job-2", 60));

        Assert.Contains("queued claim", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListQueueAsync_Deletes_Expired_Queue_File()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-23T06:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var store = new LabLeaseStore(fileSystem, timeProvider);
        var queue = await store.EnqueueAsync("usb-1", "ci-job-1", 2);
        timeProvider.Advance(TimeSpan.FromSeconds(3));

        var result = await store.ListQueueAsync();

        Assert.Equal(0, result.Count);
        Assert.False(fileSystem.FileExists(queue.QueueFile));
        Assert.Contains(queue.QueueFile, fileSystem.DeletedFiles);
    }

    [Fact]
    public async Task HeartbeatQueueAsync_Preserves_Original_WaitUntil()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-23T06:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var store = new LabLeaseStore(fileSystem, timeProvider);
        var queue = await store.EnqueueAsync("usb-1", "ci-job-1", 5);
        timeProvider.Advance(TimeSpan.FromSeconds(1));

        var updated = await store.HeartbeatQueueAsync("usb-1", queue.QueueId);

        Assert.Equal(queue.WaitUntil, updated.WaitUntil);
        Assert.Equal(timeProvider.GetUtcNow(), updated.LastHeartbeatAt);
        Assert.Equal(timeProvider.GetUtcNow().AddSeconds(15), updated.HeartbeatExpiresAt);
    }

    [Fact]
    public async Task ClaimCoordinator_ClaimWaitSec_Times_Out_At_Original_Deadline()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-23T06:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var delay = new FakeDelay(timeProvider);
        var store = new LabLeaseStore(fileSystem, timeProvider);
        await store.ClaimAsync("usb-1", "ci-job-1", 10);
        var coordinator = new LabLeaseClaimCoordinator(store, delay, timeProvider);
        var expectedDeadline = timeProvider.GetUtcNow().AddSeconds(2).ToString("O");

        var error = await Assert.ThrowsAsync<UsageException>(() => coordinator.ClaimAsync("usb-1", "ci-job-2", 60, 2));

        Assert.Contains(expectedDeadline, error.Message, StringComparison.Ordinal);
        Assert.Equal([1000, 1000], delay.Calls);
        Assert.Equal(0, (await store.ListQueueAsync()).Count);
    }
}