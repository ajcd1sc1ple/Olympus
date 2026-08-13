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
    /// Hostiles within this distance of an engaged pack member join the cluster for AoE
    /// counting (lagging InCombat flags / loose tank stacks). Flood-filled so chains of
    /// adds in a pull all unlock. Distant adjacent packs stay blocked.
    /// </summary>
    public const float PackClusterLinkYalms = 12f;

    /// <summary>
    /// Whether <paramref name="enemy"/> may be selected for damage this frame.
    /// </summary>
    /// <param name="isHardTarget">Player's current hard target.</param>
    /// <param name="playerInCombat">Local player InCombat flag.</param>
    /// <param name="enemyInCombat">Enemy InCombat flag.</param>
    /// <param name="isSoleHostileInBootstrapRange">
    /// True when this enemy is the only targetable hostile within
    /// <see cref="PullBootstrapRangeYalms"/> — boss arena before either InCombat flag flips.
    /// </param>
    /// <param name="isInEngagedPackCluster">
    /// True when this enemy is within <see cref="PackClusterLinkYalms"/> of an InCombat
    /// hostile or the hard target (lagging pack adds after a pull).
    /// </param>
    public static bool IsSelectable(
        bool isHardTarget,
        bool playerInCombat,
        bool enemyInCombat,
        bool isSoleHostileInBootstrapRange,
        bool isInEngagedPackCluster = false)
    {
        if (isHardTarget)
            return true;

        if (playerInCombat)
            return true;

        if (enemyInCombat)
            return true;

        if (isInEngagedPackCluster)
            return true;

        return isSoleHostileInBootstrapRange;
    }
}
