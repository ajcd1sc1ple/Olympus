using System;
using Olympus.Services.Prediction;
using Olympus.Timeline;
using Olympus.Timeline.Models;

namespace Olympus.Rotation.ApolloCore.Helpers;

/// <summary>
/// Helper class providing unified access to mechanic predictions.
/// Uses TimelineService when available and high confidence, falls back to BossMechanicDetector.
/// </summary>
public static class TimelineHelper
{
    /// <summary>
    /// Checks if a raidwide mechanic is imminent using the best available source.
    /// Returns true if timeline predicts a raidwide within the preparation window,
    /// or if the BossMechanicDetector (fallback) predicts one.
    /// </summary>
    /// <param name="timelineService">Timeline service (may be null).</param>
    /// <param name="bossMechanicDetector">Boss mechanic detector for fallback (may be null).</param>
    /// <param name="config">Full configuration; reads Timeline.* and Healing.* sub-sections.</param>
    /// <param name="source">Output: source of the prediction ("Timeline" or "Pattern").</param>
    /// <param name="windowSeconds">Optional custom window (defaults to config.Healing.RaidwidePreparationWindow).</param>
    /// <returns>True if a raidwide is imminent.</returns>
    public static bool IsRaidwideImminent(
        ITimelineService? timelineService,
        IBossMechanicDetector? bossMechanicDetector,
        Configuration config,
        out string source,
        float? windowSeconds = null)
    {
        source = "None";
        var window = windowSeconds ?? config.Healing.RaidwidePreparationWindow;

        // Priority 1: Timeline predictions (more accurate when synced)
        if (config.Timeline.EnableTimelinePredictions &&
            timelineService is not null &&
            timelineService.IsActive &&
            timelineService.Confidence >= config.Timeline.TimelineConfidenceThreshold)
        {
            var nextRaidwide = timelineService.NextRaidwide;
            if (nextRaidwide.HasValue &&
                nextRaidwide.Value.SecondsUntil <= window &&
                nextRaidwide.Value.SecondsUntil > 0)
            {
                source = "Timeline";
                return true;
            }
        }

        // Priority 2: Boss mechanic detector (reactive pattern detection)
        if (config.Healing.EnableMechanicAwareness && bossMechanicDetector is not null)
        {
            if (bossMechanicDetector.IsRaidwideImminent)
            {
                source = "Pattern";
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Raidwide check for GCD heal/shield prep only.
    /// Uses <see cref="ITimelineService.NextRaidwideForGcdHealPrep"/> which ignores BossMod
    /// cast-hints (bomb/bait AoEs). Falls back to pattern detection like
    /// <see cref="IsRaidwideImminent"/>.
    /// </summary>
    public static bool IsRaidwideImminentForGcdHealPrep(
        ITimelineService? timelineService,
        IBossMechanicDetector? bossMechanicDetector,
        Configuration config,
        out string source,
        float? windowSeconds = null)
    {
        source = "None";
        var window = windowSeconds ?? config.Healing.RaidwidePreparationWindow;

        if (config.Timeline.EnableTimelinePredictions &&
            timelineService is not null &&
            timelineService.IsActive &&
            timelineService.Confidence >= config.Timeline.TimelineConfidenceThreshold)
        {
            var nextRaidwide = timelineService.NextRaidwideForGcdHealPrep;
            if (nextRaidwide.HasValue &&
                nextRaidwide.Value.SecondsUntil <= window &&
                nextRaidwide.Value.SecondsUntil > 0)
            {
                source = "Timeline";
                return true;
            }
        }

        if (config.Healing.EnableMechanicAwareness && bossMechanicDetector is not null)
        {
            if (bossMechanicDetector.IsRaidwideImminent)
            {
                source = "Pattern";
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Stack / shared party-damage check for GCD AoE shield prep (Succor, Helios, E.Prognosis).
    /// Uses BossMod <c>PredictedDamageType.Shared</c> (e.g. Burning Coals) or Cactbot Stack entries.
    /// Does not use Raidwide cast-hints (spreads/bait AoEs).
    /// </summary>
    public static bool IsStackImminentForGcdHealPrep(
        ITimelineService? timelineService,
        Configuration config,
        out string source,
        float? windowSeconds = null)
    {
        source = "None";
        var window = windowSeconds ?? config.Healing.RaidwidePreparationWindow;

        if (config.Timeline.EnableTimelinePredictions &&
            timelineService is not null &&
            timelineService.IsActive &&
            timelineService.Confidence >= config.Timeline.TimelineConfidenceThreshold)
        {
            var nextStack = timelineService.NextStackForGcdHealPrep;
            if (nextStack.HasValue &&
                nextStack.Value.SecondsUntil <= window &&
                nextStack.Value.SecondsUntil > 0)
            {
                source = "Stack";
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when GCD AoE shields should be applied: imminent raidwide (Timeline/Cactbot) or
    /// stack / shared damage (BossMod Shared / Cactbot Stack).
    /// </summary>
    public static bool IsAoEShieldPrepImminent(
        ITimelineService? timelineService,
        IBossMechanicDetector? bossMechanicDetector,
        Configuration config,
        out string source,
        float? windowSeconds = null)
    {
        if (IsRaidwideImminentForGcdHealPrep(
                timelineService, bossMechanicDetector, config, out source, windowSeconds))
            return true;

        return IsStackImminentForGcdHealPrep(timelineService, config, out source, windowSeconds);
    }

    /// <summary>
    /// Checks if a tank buster is imminent using the best available source.
    /// Returns true if timeline predicts a tank buster within the preparation window,
    /// or if the BossMechanicDetector (fallback) predicts one.
    /// </summary>
    /// <param name="timelineService">Timeline service (may be null).</param>
    /// <param name="bossMechanicDetector">Boss mechanic detector for fallback (may be null).</param>
    /// <param name="config">Full configuration; reads Timeline.* and Healing.* sub-sections.</param>
    /// <param name="source">Output: source of the prediction ("Timeline" or "Pattern").</param>
    /// <returns>True if a tank buster is imminent.</returns>
    public static bool IsTankBusterImminent(
        ITimelineService? timelineService,
        IBossMechanicDetector? bossMechanicDetector,
        Configuration config,
        out string source)
    {
        source = "None";

        // Priority 1: Timeline predictions (more accurate when synced)
        if (config.Timeline.EnableTimelinePredictions &&
            timelineService is not null &&
            timelineService.IsActive &&
            timelineService.Confidence >= config.Timeline.TimelineConfidenceThreshold)
        {
            var nextTankBuster = timelineService.NextTankBuster;
            if (nextTankBuster.HasValue &&
                nextTankBuster.Value.SecondsUntil <= config.Healing.TankBusterPreparationWindow &&
                nextTankBuster.Value.SecondsUntil > 0)
            {
                source = "Timeline";
                return true;
            }
        }

        // Priority 2: Boss mechanic detector (reactive pattern detection)
        if (config.Healing.EnableMechanicAwareness && bossMechanicDetector is not null)
        {
            if (bossMechanicDetector.IsTankBusterImminent)
            {
                source = "Pattern";
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Tank-buster check for GCD shield prep only (ignores BossMod cast-hints).
    /// </summary>
    public static bool IsTankBusterImminentForGcdHealPrep(
        ITimelineService? timelineService,
        IBossMechanicDetector? bossMechanicDetector,
        Configuration config,
        out string source)
    {
        source = "None";

        if (config.Timeline.EnableTimelinePredictions &&
            timelineService is not null &&
            timelineService.IsActive &&
            timelineService.Confidence >= config.Timeline.TimelineConfidenceThreshold)
        {
            var nextTankBuster = timelineService.NextTankBusterForGcdHealPrep;
            if (nextTankBuster.HasValue &&
                nextTankBuster.Value.SecondsUntil <= config.Healing.TankBusterPreparationWindow &&
                nextTankBuster.Value.SecondsUntil > 0)
            {
                source = "Timeline";
                return true;
            }
        }

        if (config.Healing.EnableMechanicAwareness && bossMechanicDetector is not null)
        {
            if (bossMechanicDetector.IsTankBusterImminent)
            {
                source = "Pattern";
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets the next raidwide prediction with timing information.
    /// Prefers timeline predictions when available and high confidence.
    /// </summary>
    /// <param name="timelineService">Timeline service (may be null).</param>
    /// <param name="bossMechanicDetector">Boss mechanic detector for fallback (may be null).</param>
    /// <param name="config">Full configuration; reads Timeline.* and Healing.* sub-sections.</param>
    /// <returns>Prediction info or null if none available.</returns>
    public static (float secondsUntil, float confidence, string name, string source)? GetNextRaidwide(
        ITimelineService? timelineService,
        IBossMechanicDetector? bossMechanicDetector,
        Configuration config)
    {
        // Priority 1: Timeline predictions
        if (config.Timeline.EnableTimelinePredictions &&
            timelineService is not null &&
            timelineService.IsActive &&
            timelineService.Confidence >= config.Timeline.TimelineConfidenceThreshold)
        {
            var nextRaidwide = timelineService.NextRaidwide;
            if (nextRaidwide.HasValue && nextRaidwide.Value.SecondsUntil > 0)
            {
                return (nextRaidwide.Value.SecondsUntil,
                        nextRaidwide.Value.Confidence,
                        nextRaidwide.Value.Name,
                        "Timeline");
            }
        }

        // Priority 2: Boss mechanic detector
        if (config.Healing.EnableMechanicAwareness && bossMechanicDetector is not null)
        {
            var prediction = bossMechanicDetector.PredictedRaidwide;
            if (prediction is not null && prediction.SecondsUntil > 0)
            {
                return (prediction.SecondsUntil,
                        prediction.Confidence,
                        "Raidwide (pattern)",
                        "Pattern");
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the next tank buster prediction with timing information.
    /// Prefers timeline predictions when available and high confidence.
    /// </summary>
    /// <param name="timelineService">Timeline service (may be null).</param>
    /// <param name="bossMechanicDetector">Boss mechanic detector for fallback (may be null).</param>
    /// <param name="config">Full configuration; reads Timeline.* and Healing.* sub-sections.</param>
    /// <returns>Prediction info or null if none available.</returns>
    public static (float secondsUntil, float confidence, string name, string source)? GetNextTankBuster(
        ITimelineService? timelineService,
        IBossMechanicDetector? bossMechanicDetector,
        Configuration config)
    {
        // Priority 1: Timeline predictions
        if (config.Timeline.EnableTimelinePredictions &&
            timelineService is not null &&
            timelineService.IsActive &&
            timelineService.Confidence >= config.Timeline.TimelineConfidenceThreshold)
        {
            var nextTankBuster = timelineService.NextTankBuster;
            if (nextTankBuster.HasValue && nextTankBuster.Value.SecondsUntil > 0)
            {
                return (nextTankBuster.Value.SecondsUntil,
                        nextTankBuster.Value.Confidence,
                        nextTankBuster.Value.Name,
                        "Timeline");
            }
        }

        // Priority 2: Boss mechanic detector
        if (config.Healing.EnableMechanicAwareness && bossMechanicDetector is not null)
        {
            var prediction = bossMechanicDetector.PredictedTankBuster;
            if (prediction is not null && prediction.SecondsUntil > 0)
            {
                return (prediction.SecondsUntil,
                        prediction.Confidence,
                        "Tank Buster (pattern)",
                        "Pattern");
            }
        }

        return null;
    }

    /// <summary>
    /// Checks if the timeline is currently providing high-confidence predictions.
    /// Used to determine whether to use timeline-based decisions or fall back.
    /// </summary>
    /// <param name="timelineService">Timeline service (may be null).</param>
    /// <param name="config">Full configuration; reads Timeline.* sub-section.</param>
    /// <returns>True if timeline is active and above confidence threshold.</returns>
    public static bool IsTimelineHighConfidence(
        ITimelineService? timelineService,
        Configuration config)
    {
        return config.Timeline.EnableTimelinePredictions &&
               timelineService is not null &&
               timelineService.IsActive &&
               timelineService.Confidence >= config.Timeline.TimelineConfidenceThreshold;
    }

    /// <summary>
    /// Gets a status string for timeline state (for debug display).
    /// </summary>
    /// <param name="timelineService">Timeline service (may be null).</param>
    /// <param name="config">Full configuration; reads Timeline.* sub-section.</param>
    /// <returns>Human-readable status string.</returns>
    public static string GetTimelineStatus(
        ITimelineService? timelineService,
        Configuration config)
    {
        if (!config.Timeline.EnableTimelinePredictions)
            return "Disabled";

        if (timelineService is null)
            return "Not initialized";

        if (!timelineService.IsActive)
            return "No timeline";

        var confidencePercent = timelineService.Confidence * 100f;
        var fightName = timelineService.FightName;

        if (timelineService.Confidence >= config.Timeline.TimelineConfidenceThreshold)
        {
            var source = timelineService.NextRaidwide?.Name.StartsWith("BossMod", StringComparison.Ordinal) == true
                || timelineService.NextTankBuster?.Name.StartsWith("BossMod", StringComparison.Ordinal) == true
                ? "BossMod"
                : "Cactbot";
            return $"{fightName} [{confidencePercent:F0}% · {source}]";
        }

        return $"{fightName} [Low: {confidencePercent:F0}%]";
    }

    /// <summary>
    /// Resolves the tank (or best available party member) for tank-buster mitigation/shield prep.
    /// Prefers <see cref="IPartyHelper.FindTankInParty"/>; falls back to any living non-self
    /// party member so full-HP tanks are still shielded when role detection is unavailable.
    /// Does not use FindLowestHpPartyMember (which skips full-HP targets).
    /// </summary>
    /// <summary>
    /// Resolves the tank (or best available party member) for tank-buster mitigation/shield prep.
    /// Prefers an explicit tank when provided; falls back to any living non-self party member
    /// so full-HP tanks are still shielded (FindLowestHpPartyMember skips full HP).
    /// </summary>
    public static Dalamud.Game.ClientState.Objects.Types.IBattleChara? ResolveTankBusterTarget(
        Dalamud.Game.ClientState.Objects.Types.IBattleChara? tank,
        System.Collections.Generic.IEnumerable<Dalamud.Game.ClientState.Objects.Types.IBattleChara> partyMembers,
        uint playerEntityId)
    {
        if (tank != null)
            return tank;

        foreach (var member in partyMembers)
        {
            if (member.IsDead)
                continue;
            if (member.EntityId == playerEntityId)
                continue;
            return member;
        }

        return null;
    }
}
