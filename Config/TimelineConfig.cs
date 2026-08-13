using System;

namespace Olympus.Config;

/// <summary>
/// Cross-role timeline behavior settings. Controls whether rotations trust
/// timeline mechanic predictions and whether cast-time damage spells are
/// blocked before predicted mechanics.
/// </summary>
public sealed class TimelineConfig
{
    /// <summary>
    /// Master toggle for timeline-based mechanic predictions.
    /// When disabled, rotations ignore timeline data entirely.
    /// </summary>
    public bool EnableTimelinePredictions { get; set; } = true;

    /// <summary>
    /// When true, prefer BossMod / BossMod Reborn timeline IPC (raidwide / tankbuster /
    /// downtime) when an encounter module is active, merged with embedded Cactbot timelines.
    /// Default on — same approach as Rotation Solver Reborn.
    /// </summary>
    public bool EnableBossModTimelineIntegration { get; set; } = true;

    /// <summary>
    /// Minimum timeline confidence required to trust predictions.
    /// Timeline confidence decays over time since the last sync point.
    /// Valid range: 0.5 to 1.0.
    /// </summary>
    private float _timelineConfidenceThreshold = 0.8f;
    public float TimelineConfidenceThreshold
    {
        get => _timelineConfidenceThreshold;
        set => _timelineConfidenceThreshold = Math.Clamp(value, 0.5f, 1f);
    }

    /// <summary>
    /// Legacy toggle retained for config compatibility. Cast-time damage is no longer
    /// blocked before raidwides/tankbusters (cast-through for all roles). Tank-buster
    /// mitigations still use timeline predictions independently.
    /// </summary>
    public bool EnableMechanicAwareCasting { get; set; } = false;
}
