using Olympus.Timeline.Models;

namespace Olympus.Timeline;

/// <summary>
/// Pure merge helpers for combining BossMod Timeline IPC with embedded Cactbot timelines.
/// One BossMod channel per mechanic type — no Hints dual-source merge.
/// When BossMod has a prediction it wins; Cactbot is fallback only.
/// </summary>
public static class BossModTimelineMerge
{
    public const float BossModConfidence = 0.95f;

    /// <summary>
    /// Picks the soonest positive prediction. Nulls are ignored.
    /// </summary>
    public static MechanicPrediction? PreferSoonest(MechanicPrediction? a, MechanicPrediction? b)
    {
        if (a is null)
            return b;
        if (b is null)
            return a;

        return a.Value.SecondsUntil <= b.Value.SecondsUntil ? a : b;
    }

    public static MechanicPrediction? FromSeconds(float? secondsUntil, TimelineEntryType type, string name)
    {
        if (secondsUntil is not { } t || t <= 0f)
            return null;

        return new MechanicPrediction(t, type, name, BossModConfidence);
    }

    /// <summary>
    /// Uses BossMod Timeline.NextRaidwideIn when present; otherwise Cactbot.
    /// </summary>
    public static MechanicPrediction? MergeRaidwide(float? bossModTimelineSeconds, MechanicPrediction? cactbot)
    {
        return FromSeconds(bossModTimelineSeconds, TimelineEntryType.Raidwide, "BossMod raidwide")
               ?? cactbot;
    }

    /// <summary>
    /// Uses BossMod Timeline.NextTankbusterIn when present; otherwise Cactbot.
    /// </summary>
    public static MechanicPrediction? MergeTankBuster(float? bossModTimelineSeconds, MechanicPrediction? cactbot)
    {
        return FromSeconds(bossModTimelineSeconds, TimelineEntryType.TankBuster, "BossMod tankbuster")
               ?? cactbot;
    }
}
