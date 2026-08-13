using Olympus.Timeline.Models;

namespace Olympus.Timeline;

/// <summary>
/// Pure merge helpers for combining BossMod IPC with embedded Cactbot timelines.
/// Resolves one BossMod value per mechanic (cast-hint preferred, else Timeline),
/// then uses Cactbot only when BossMod has no prediction.
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
    /// One BossMod answer: prefer cast-hint (actual damage timing) when present,
    /// otherwise Timeline state-machine. Cactbot is fallback only.
    /// </summary>
    public static MechanicPrediction? MergeRaidwide(
        float? timelineSeconds,
        float? hintSeconds,
        MechanicPrediction? cactbot)
    {
        var bossMod = FromSeconds(hintSeconds, TimelineEntryType.Raidwide, "BossMod raidwide")
                      ?? FromSeconds(timelineSeconds, TimelineEntryType.Raidwide, "BossMod raidwide");
        return bossMod ?? cactbot;
    }

    /// <summary>
    /// One BossMod answer: prefer cast-hint when present, otherwise Timeline.
    /// Cactbot is fallback only.
    /// </summary>
    public static MechanicPrediction? MergeTankBuster(
        float? timelineSeconds,
        float? hintSeconds,
        MechanicPrediction? cactbot)
    {
        var bossMod = FromSeconds(hintSeconds, TimelineEntryType.TankBuster, "BossMod tankbuster")
                      ?? FromSeconds(timelineSeconds, TimelineEntryType.TankBuster, "BossMod tankbuster");
        return bossMod ?? cactbot;
    }
}
