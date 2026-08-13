using Olympus.Timeline;
using Olympus.Timeline.Models;
using Xunit;

namespace Olympus.Tests.Timeline;

public class BossModTimelineMergeTests
{
    [Fact]
    public void PreferSoonest_PicksEarlier()
    {
        var a = new MechanicPrediction(8f, TimelineEntryType.Raidwide, "A", 0.9f);
        var b = new MechanicPrediction(3f, TimelineEntryType.Raidwide, "B", 0.9f);

        var result = BossModTimelineMerge.PreferSoonest(a, b);
        Assert.NotNull(result);
        Assert.Equal("B", result!.Value.Name);
    }

    [Fact]
    public void PreferSoonest_NullFallsThrough()
    {
        var a = new MechanicPrediction(5f, TimelineEntryType.Raidwide, "A", 0.9f);
        Assert.Equal("A", BossModTimelineMerge.PreferSoonest(a, null)!.Value.Name);
        Assert.Equal("A", BossModTimelineMerge.PreferSoonest(null, a)!.Value.Name);
        Assert.Null(BossModTimelineMerge.PreferSoonest(null, null));
    }

    [Fact]
    public void MergeRaidwide_UsesBossModTimelineWhenPresent()
    {
        var cactbot = new MechanicPrediction(3f, TimelineEntryType.Raidwide, "Cactbot RW", 0.85f);
        // BossMod later than Cactbot still wins — single Timeline API, not PreferSoonest.
        var merged = BossModTimelineMerge.MergeRaidwide(12f, cactbot);

        Assert.NotNull(merged);
        Assert.Equal("BossMod raidwide", merged!.Value.Name);
        Assert.Equal(12f, merged.Value.SecondsUntil);
        Assert.Equal(BossModTimelineMerge.BossModConfidence, merged.Value.Confidence);
    }

    [Fact]
    public void MergeRaidwide_KeepsCactbotWhenBossModAbsent()
    {
        var cactbot = new MechanicPrediction(7f, TimelineEntryType.Raidwide, "Cactbot RW", 0.85f);
        var merged = BossModTimelineMerge.MergeRaidwide(null, cactbot);

        Assert.NotNull(merged);
        Assert.Equal("Cactbot RW", merged!.Value.Name);
    }

    [Fact]
    public void MergeTankBuster_UsesBossModTimelineOnly()
    {
        var cactbot = new MechanicPrediction(2.5f, TimelineEntryType.TankBuster, "Cactbot TB", 0.85f);
        var merged = BossModTimelineMerge.MergeTankBuster(10f, cactbot);

        Assert.NotNull(merged);
        Assert.Equal("BossMod tankbuster", merged!.Value.Name);
        Assert.Equal(10f, merged.Value.SecondsUntil);
    }

    [Fact]
    public void FromSeconds_RejectsNonPositive()
    {
        Assert.Null(BossModTimelineMerge.FromSeconds(null, TimelineEntryType.Raidwide, "x"));
        Assert.Null(BossModTimelineMerge.FromSeconds(0f, TimelineEntryType.Raidwide, "x"));
        Assert.Null(BossModTimelineMerge.FromSeconds(-1f, TimelineEntryType.Raidwide, "x"));
    }
}
