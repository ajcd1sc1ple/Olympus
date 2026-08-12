using Olympus.Timeline.Models;

namespace Olympus.Timeline;

/// <summary>
/// Pure merge helpers for combining BossMod IPC predictions with embedded Cactbot timelines.
/// Prefers the soonest credible prediction.
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
    /// Combines state-machine timeline + cast-hint channels, then merges with the local Cactbot prediction.
    /// </summary>
    public static MechanicPrediction? MergeRaidwide(
        float? timelineSeconds,
        float? hintSeconds,
        MechanicPrediction? cactbot)
    {
        var bossMod = PreferSoonest(
            FromSeconds(timelineSeconds, TimelineEntryType.Raidwide, "BossMod raidwide"),
            FromSeconds(hintSeconds, TimelineEntryType.Raidwide, "BossMod raidwide (cast)"));
        return PreferSoonest(bossMod, cactbot);
    }

    public static MechanicPrediction? MergeTankBuster(
        float? timelineSeconds,
        float? hintSeconds,
        MechanicPrediction? cactbot)
    {
        var bossMod = PreferSoonest(
            FromSeconds(timelineSeconds, TimelineEntryType.TankBuster, "BossMod tankbuster"),
            FromSeconds(hintSeconds, TimelineEntryType.TankBuster, "BossMod tankbuster (cast)"));
        return PreferSoonest(bossMod, cactbot);
    }
}
