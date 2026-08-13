namespace Olympus.Services.Targeting;

/// <summary>
/// Pure decision logic for <see cref="Config.TargetingConfig.PauseWhenNoTarget"/>.
/// Distinguishes intentional gaze / disengage (sustained null hard target) from the
/// 1–2 frame null gap when the player Tabs between enemies mid-pack.
/// </summary>
public static class DamagePauseDecision
{
    /// <summary>
    /// How long the hard target may be null before PauseWhenNoTarget commits.
    /// Tab retarget is typically one client frame; gaze / intentional drop is held for seconds.
    /// </summary>
    public const int NoTargetGraceMs = 200;

    /// <summary>
    /// Whether damage targeting should be suppressed this frame.
    /// </summary>
    /// <param name="pauseWhenNoTarget">Config toggle.</param>
    /// <param name="hasHardTarget">Player currently has a hard target (any object).</param>
    /// <param name="playerInCombat">
    /// Local player's <c>StatusFlags.InCombat</c>. When false, never pause — the engagement
    /// filter already blocks unpulled packs, and healers need Count/Find to see tank-engaged
    /// enemies for combat bootstrap without a hard target. When null, combat state is unknown
    /// (parameterless callers) and only the grace timer applies.
    /// </param>
    /// <param name="noTargetDurationMs">How long the hard target has been continuously null.</param>
    /// <param name="graceMs">Grace before a null target counts as an intentional pause.</param>
    public static bool ShouldPause(
        bool pauseWhenNoTarget,
        bool hasHardTarget,
        bool? playerInCombat,
        long noTargetDurationMs,
        int graceMs = NoTargetGraceMs)
    {
        if (!pauseWhenNoTarget || hasHardTarget)
            return false;

        // Out of combat: null hard target is not a pause signal.
        if (playerInCombat == false)
            return false;

        return noTargetDurationMs >= graceMs;
    }

    /// <summary>
    /// Whether Strict CurrentTarget/FocusTarget may still fall back to LowestHp.
    /// During the retarget grace window a brief null must not stall DPS; after grace,
    /// strict mode stays empty so drop-target remains a hard stop (gaze).
    /// </summary>
    public static bool AllowExplicitTargetFallback(
        bool strictCurrentTargetStrategy,
        bool hasHardTarget,
        long noTargetDurationMs,
        int graceMs = NoTargetGraceMs)
    {
        if (!strictCurrentTargetStrategy)
            return true;

        if (hasHardTarget)
            return false;

        return noTargetDurationMs < graceMs;
    }
}
