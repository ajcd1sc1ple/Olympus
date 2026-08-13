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
    public void MergeRaidwide_PrefersCastHintOverTimeline()
    {
        var cactbot = new MechanicPrediction(12f, TimelineEntryType.Raidwide, "Cactbot RW", 0.85f);
        var merged = BossModTimelineMerge.MergeRaidwide(timelineSeconds: 4f, hintSeconds: 2f, cactbot);

        Assert.NotNull(merged);
        Assert.Equal("BossMod raidwide (cast)", merged!.Value.Name);
        Assert.Equal(2f, merged.Value.SecondsUntil);
    }

    [Fact]
    public void MergeRaidwide_UsesTimelineWhenHintAbsent()
    {
        var cactbot = new MechanicPrediction(3f, TimelineEntryType.Raidwide, "Cactbot RW", 0.85f);
        var merged = BossModTimelineMerge.MergeRaidwide(timelineSeconds: 12f, hintSeconds: null, cactbot);

        Assert.NotNull(merged);
        Assert.Equal("BossMod raidwide (timeline)", merged!.Value.Name);
        Assert.Equal(12f, merged.Value.SecondsUntil);
        Assert.Equal(BossModTimelineMerge.BossModConfidence, merged.Value.Confidence);
    }

    [Fact]
    public void MergeRaidwide_KeepsCactbotWhenBossModAbsent()
    {
        var cactbot = new MechanicPrediction(7f, TimelineEntryType.Raidwide, "Cactbot RW", 0.85f);
        var merged = BossModTimelineMerge.MergeRaidwide(null, null, cactbot);

        Assert.NotNull(merged);
        Assert.Equal("Cactbot RW", merged!.Value.Name);
    }

    [Fact]
    public void MergeRaidwideForGcdHealPrep_IgnoresCastHint()
    {
        // Anthracite-style: cast-hint fires on every bomb AoE (2s) while the real
        // Timeline raidwide is further out (12s). GCD prep must use Timeline only.
        var cactbot = new MechanicPrediction(20f, TimelineEntryType.Raidwide, "Cactbot RW", 0.85f);
        var merged = BossModTimelineMerge.MergeRaidwideForGcdHealPrep(
            timelineSeconds: 12f, hintSeconds: 2f, cactbot);

        Assert.NotNull(merged);
        Assert.Equal("BossMod raidwide (timeline)", merged!.Value.Name);
        Assert.Equal(12f, merged.Value.SecondsUntil);
    }

    [Fact]
    public void MergeRaidwideForGcdHealPrep_FallsBackToCactbotWhenNoTimeline()
    {
        var cactbot = new MechanicPrediction(7f, TimelineEntryType.Raidwide, "Cactbot RW", 0.85f);
        var merged = BossModTimelineMerge.MergeRaidwideForGcdHealPrep(
            timelineSeconds: null, hintSeconds: 1.5f, cactbot);

        Assert.NotNull(merged);
        Assert.Equal("Cactbot RW", merged!.Value.Name);
    }

    [Fact]
    public void MergeTankBuster_PrefersCastHintOverTimeline()
    {
        var merged = BossModTimelineMerge.MergeTankBuster(10f, 2.5f, null);

        Assert.NotNull(merged);
        Assert.Equal("BossMod tankbuster (cast)", merged!.Value.Name);
        Assert.Equal(2.5f, merged.Value.SecondsUntil);
    }

    [Fact]
    public void MergeTankBusterForGcdHealPrep_IgnoresCastHint()
    {
        var merged = BossModTimelineMerge.MergeTankBusterForGcdHealPrep(8f, 1f, null);

        Assert.NotNull(merged);
        Assert.Equal("BossMod tankbuster (timeline)", merged!.Value.Name);
        Assert.Equal(8f, merged.Value.SecondsUntil);
    }

    [Fact]
    public void FromSeconds_StackShared_BuildsPrediction()
    {
        var merged = BossModTimelineMerge.FromSeconds(4.5f, TimelineEntryType.Stack, "BossMod stack (shared)");
        Assert.NotNull(merged);
        Assert.Equal(TimelineEntryType.Stack, merged!.Value.Type);
        Assert.Equal(4.5f, merged.Value.SecondsUntil);
        Assert.Equal("BossMod stack (shared)", merged.Value.Name);
    }

    [Fact]
    public void FromSeconds_RejectsNonPositive()
    {
        Assert.Null(BossModTimelineMerge.FromSeconds(null, TimelineEntryType.Raidwide, "x"));
        Assert.Null(BossModTimelineMerge.FromSeconds(0f, TimelineEntryType.Raidwide, "x"));
        Assert.Null(BossModTimelineMerge.FromSeconds(-1f, TimelineEntryType.Raidwide, "x"));
    }
}
