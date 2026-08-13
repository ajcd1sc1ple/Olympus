namespace Olympus.Rotation.Common.Scheduling;

/// <summary>
/// Shared healer scheduler priority bands. Lower values win within a queue
/// (GCD and oGCD are separate queues that reuse these bands).
/// <para>
/// Intended order: raise/prep → <b>timeline mitigation</b> → healing →
/// reactive mitigation → buffs → DPS.
/// </para>
/// </summary>
public static class HealerSchedulerPriorities
{
    /// <summary>
    /// Timeline-driven mitigations and shield prep (raidwide / tank buster /
    /// stack). Beats reactive healing so mit lands before the hit.
    /// </summary>
    public const int TimelineMitigation = 8;

    /// <summary>
    /// Tank-buster timeline mit offset within the timeline band.
    /// </summary>
    public const int TimelineTankBusterOffset = 1;

    /// <summary>
    /// Default priority for reactive / HP-threshold mitigations (after heals).
    /// </summary>
    public const int ReactiveMitigation = 90;

    /// <summary>
    /// Priority for a mitigation candidate: timeline-driven uses the timeline
    /// band; otherwise uses <paramref name="reactivePriority"/>.
    /// </summary>
    public static int Mitigation(
        bool timelineDriven,
        int timelineOffset = 0,
        int reactivePriority = ReactiveMitigation)
        => timelineDriven
            ? TimelineMitigation + timelineOffset
            : reactivePriority;
}
