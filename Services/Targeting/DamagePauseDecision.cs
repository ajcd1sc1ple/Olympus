namespace Olympus.Services.Targeting;

/// <summary>
/// Damage-targeting pause / fallback helpers.
/// <para>
/// Historical <c>PauseWhenNoTarget</c> stalls (gaze / drop-target) froze DPS on dual-boss
/// swaps (Akadaemia Anyder sharks), Tab retargets, and BossMod pathing clears. Those stalls
/// are removed — engagement filtering already prevents pulling unengaged packs, and
/// LowestHp / Find keep attacking whatever is selectable.
/// </para>
/// </summary>
public static class DamagePauseDecision
{
    /// <summary>
    /// Retained for call-site / test compatibility. No longer used to stall damage.
    /// </summary>
    public const int NoTargetGraceMs = 200;

    /// <summary>
    /// Always false — damage targeting is never suppressed for a null hard target.
    /// </summary>
    public static bool ShouldPause(
        bool pauseWhenNoTarget,
        bool hasHardTarget,
        bool? playerInCombat,
        long noTargetDurationMs,
        int graceMs = NoTargetGraceMs)
    {
        _ = (pauseWhenNoTarget, hasHardTarget, playerInCombat, noTargetDurationMs, graceMs);
        return false;
    }

    /// <summary>
    /// Whether CurrentTarget/FocusTarget may fall back to LowestHp.
    /// Falls back whenever the hard target is missing or not usable (untargetable dive),
    /// so dual-boss water swaps do not stall DPS on the underwater shark.
    /// </summary>
    public static bool AllowExplicitTargetFallback(
        bool strictCurrentTargetStrategy,
        bool hasHardTarget,
        long noTargetDurationMs,
        int graceMs = NoTargetGraceMs,
        bool hardTargetUsable = true)
    {
        _ = (noTargetDurationMs, graceMs);

        if (!strictCurrentTargetStrategy)
            return true;

        // Usable hard target — honor it (no fallback spill).
        if (hasHardTarget && hardTargetUsable)
            return false;

        // Null or untargetable hard target — keep DPS on another selectable enemy.
        return true;
    }
}
