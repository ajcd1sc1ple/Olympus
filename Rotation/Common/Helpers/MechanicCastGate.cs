using Olympus.Timeline;

namespace Olympus.Rotation.Common.Helpers;

/// <summary>
/// Shared decision logic for optionally blocking cast-time damage GCDs when a
/// raidwide or tank buster is predicted to hit before the cast would complete.
///
/// Casting-through is the default for all roles: holding hardcasts without a
/// reliable stationary instant filler created empty GCD windows around every
/// timeline hit (healers, casters, and tanks). Tank-buster mitigations are
/// unrelated — they still fire from <c>NextTankBuster</c> / timeline helpers.
///
/// Instant GCDs (castTime &lt;= 0) always return false.
/// </summary>
public static class MechanicCastGate
{
    /// <summary>
    /// Returns whether a cast-time damage GCD should be held for an imminent mechanic.
    /// Always false: cast-through is required for continuous GCD uptime.
    /// The timeline toggle is retained for config compatibility but no longer blocks.
    /// </summary>
    public static bool ShouldBlock(IRotationContext context, float castTime)
    {
        _ = context;
        _ = castTime;
        return false;
    }

    /// <summary>
    /// Produces a human-readable debug string describing why the gate would block,
    /// for surfacing in a module's debug state field.
    /// </summary>
    public static string FormatBlockedState(IRotationContext context)
    {
        var timeline = context.TimelineService;
        if (timeline == null) return "Held cast (mechanic)";

        var rw = timeline.NextRaidwide;
        var tb = timeline.NextTankBuster;

        bool rwCloser = rw.HasValue && (!tb.HasValue || rw.Value.SecondsUntil <= tb.Value.SecondsUntil);
        if (rwCloser && rw.HasValue)
            return $"Held cast (raidwide in {rw.Value.SecondsUntil:F1}s)";
        if (tb.HasValue)
            return $"Held cast (tank buster in {tb.Value.SecondsUntil:F1}s)";
        return "Held cast (mechanic)";
    }
}
