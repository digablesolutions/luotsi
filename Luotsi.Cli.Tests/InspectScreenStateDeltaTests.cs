using Luotsi.Cli.Cli.Inspect;
using Luotsi.Cli.Models;
using Xunit;

namespace Luotsi.Cli.Tests;

public sealed class InspectScreenStateDeltaTests
{
    [Fact]
    public void Create_handles_duplicate_stable_ids()
    {
        var capturedAt = DateTimeOffset.Parse("2026-05-31T00:00:00Z");
        var previous = new ScreenState(
            capturedAt,
            2,
            [
                CreateClassOnlyElement(0, 0, 100, 100),
                CreateClassOnlyElement(0, 100, 100, 200)
            ]);
        var current = new ScreenState(
            capturedAt.AddSeconds(1),
            2,
            [
                CreateClassOnlyElement(0, 0, 100, 100),
                CreateClassOnlyElement(0, 120, 100, 220)
            ]);

        var delta = InspectScreenStateDelta.Create(previous, current);

        Assert.Empty(delta.Added);
        Assert.Empty(delta.Removed);
        Assert.Single(delta.Changed);
        Assert.Equal("android.widget.LinearLayout#2", delta.Changed[0].StableId);
    }

    [Fact]
    public void Create_removes_duplicate_without_relabeling_unchanged_siblings()
    {
        var capturedAt = DateTimeOffset.Parse("2026-05-31T00:00:00Z");
        var top = CreateClassOnlyElement(0, 0, 100, 100);
        var middle = CreateClassOnlyElement(0, 100, 100, 200);
        var bottom = CreateClassOnlyElement(0, 200, 100, 300);
        var previous = new ScreenState(
            capturedAt,
            3,
            [top, middle, bottom]);
        var current = new ScreenState(
            capturedAt.AddSeconds(1),
            2,
            [top, bottom]);

        var delta = InspectScreenStateDelta.Create(previous, current);

        Assert.Empty(delta.Added);
        Assert.Empty(delta.Changed);
        Assert.Equal(["android.widget.LinearLayout#2"], delta.Removed);
    }

    [Fact]
    public void Create_inserts_duplicate_without_relabeling_unchanged_siblings()
    {
        var capturedAt = DateTimeOffset.Parse("2026-05-31T00:00:00Z");
        var top = CreateClassOnlyElement(0, 0, 100, 100);
        var middle = CreateClassOnlyElement(0, 100, 100, 200);
        var bottom = CreateClassOnlyElement(0, 200, 100, 300);
        var previous = new ScreenState(
            capturedAt,
            2,
            [top, bottom]);
        var current = new ScreenState(
            capturedAt.AddSeconds(1),
            3,
            [top, middle, bottom]);

        var delta = InspectScreenStateDelta.Create(previous, current);

        Assert.Single(delta.Added);
        Assert.Equal(middle, delta.Added[0]);
        Assert.Empty(delta.Changed);
        Assert.Empty(delta.Removed);
    }

    [Fact]
    public void CreateHash_is_stable_for_reordered_duplicate_stable_ids()
    {
        var capturedAt = DateTimeOffset.Parse("2026-05-31T00:00:00Z");
        var top = CreateClassOnlyElement(0, 0, 100, 100);
        var bottom = CreateClassOnlyElement(0, 200, 100, 300);
        var previous = new ScreenState(
            capturedAt,
            2,
            [top, bottom]);
        var current = new ScreenState(
            capturedAt.AddSeconds(1),
            2,
            [bottom, top]);

        var delta = InspectScreenStateDelta.Create(previous, current);

        Assert.Equal(delta.PreviousHash, delta.CurrentHash);
        Assert.Empty(delta.Added);
        Assert.Empty(delta.Changed);
        Assert.Empty(delta.Removed);
    }

    private static ScreenElement CreateClassOnlyElement(int left, int top, int right, int bottom) =>
        new(null, null, null, "android.widget.LinearLayout", true, false, left, top, right, bottom);
}
