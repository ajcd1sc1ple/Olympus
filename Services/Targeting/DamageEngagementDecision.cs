namespace Olympus.Services.Targeting;

/// <summary>
/// Pure engagement rules for damage targeting (Count / Find / AoE).
/// </summary>
public static class DamageEngagementDecision
{
    /// <summary>
    /// Range used for the OOC sole-hostile pull bootstrap (boss seal before InCombat flags).
    /// </summary>
    public const float PullBootstrapRangeYalms = 30f;

    /// <summary>
    /// Whether <paramref name="enemy"/> may be selected for damage this frame.
    /// </summary>
    /// <param name="isHardTarget">Player's current hard target.</param>
    /// <param name="playerInCombat">Local player InCombat flag.</param>
    /// <param name="enemyInCombat">Enemy InCombat flag.</param>
    /// <param name="isSoleHostileInBootstrapRange">
    /// True when this enemy is the only targetable hostile within
    /// <see cref="PullBootstrapRangeYalms"/> — boss arena before either InCombat flag flips.
    /// Multi-mob trash packs stay blocked until something is engaged.
    /// </param>
    public static bool IsSelectable(
        bool isHardTarget,
        bool playerInCombat,
        bool enemyInCombat,
        bool isSoleHostileInBootstrapRange)
    {
        if (isHardTarget)
            return true;

        if (playerInCombat)
            return true;

        if (enemyInCombat)
            return true;

        return isSoleHostileInBootstrapRange;
    }
}
