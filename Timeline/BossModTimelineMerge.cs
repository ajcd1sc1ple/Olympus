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
    /// Display / oGCD mit: prefer cast-hint (actual damage timing) when present,
    /// otherwise Timeline state-machine. Cactbot is fallback only.
    /// </summary>
    public static MechanicPrediction? MergeRaidwide(
        float? timelineSeconds,
        float? hintSeconds,
        MechanicPrediction? cactbot)
    {
        var bossMod = FromSeconds(hintSeconds, TimelineEntryType.Raidwide, "BossMod raidwide (cast)")
                      ?? FromSeconds(timelineSeconds, TimelineEntryType.Raidwide, "BossMod raidwide (timeline)");
        return bossMod ?? cactbot;
    }

    /// <summary>
    /// GCD heal/shield prep must NOT use BossMod cast-hints.
    /// <c>Hints.NextRaidwideDamageIn</c> fires for many party-hitting AoEs (Anthracite
    /// bombs, baited circles, etc.), which kept Succor/Helios/E.Prognosis at priority
    /// 10–30 for entire fights and starved DPS. Timeline + Cactbot mark real raidwides.
    /// </summary>
    public static MechanicPrediction? MergeRaidwideForGcdHealPrep(
        float? timelineSeconds,
        float? hintSeconds,
        MechanicPrediction? cactbot)
    {
        _ = hintSeconds;
        return FromSeconds(timelineSeconds, TimelineEntryType.Raidwide, "BossMod raidwide (timeline)")
               ?? cactbot;
    }

    /// <summary>
    /// Display / oGCD mit: prefer cast-hint when present, otherwise Timeline.
    /// Cactbot is fallback only.
    /// </summary>
    public static MechanicPrediction? MergeTankBuster(
        float? timelineSeconds,
        float? hintSeconds,
        MechanicPrediction? cactbot)
    {
        var bossMod = FromSeconds(hintSeconds, TimelineEntryType.TankBuster, "BossMod tankbuster (cast)")
                      ?? FromSeconds(timelineSeconds, TimelineEntryType.TankBuster, "BossMod tankbuster (timeline)");
        return bossMod ?? cactbot;
    }

    /// <summary>
    /// GCD tank-buster shield prep: keep BossMod cast-hints.
    /// Unlike raidwide hints (bombs/bait), TB cast-hints are the primary BossMod signal
    /// for many fights — dropping them left E.Diagnosis/Adlo with no prep source.
    /// </summary>
    public static MechanicPrediction? MergeTankBusterForGcdHealPrep(
        float? timelineSeconds,
        float? hintSeconds,
        MechanicPrediction? cactbot) =>
        MergeTankBuster(timelineSeconds, hintSeconds, cactbot);
}
