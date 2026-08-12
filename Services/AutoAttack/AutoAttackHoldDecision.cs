namespace Olympus.Services.AutoAttack;

/// <summary>
/// Pure decision logic for keeping game auto-attack on until the engaged enemy dies.
/// </summary>
public static class AutoAttackHoldDecision
{
    /// <summary>
    /// Desired auto-attack state for this frame, or null when management should not change it.
    /// </summary>
    /// <param name="managementEnabled">Config toggle for hold-until-dead management.</param>
    /// <param name="pluginEnabled">Master rotation enable.</param>
    /// <param name="playerAlive">Local player has HP &gt; 0.</param>
    /// <param name="inCombat">Server InCombat flag.</param>
    /// <param name="currentlyAutoAttacking">Current UI auto-attack state.</param>
    /// <param name="hasLivingHostileTarget">Hard target is a living hostile.</param>
    /// <param name="targetIsDead">Hard target exists and is dead (or HP 0).</param>
    /// <param name="engagedTargetStillAlive">Previously engaged entity is still alive (may not be hard-targeted).</param>
    /// <param name="engagedTargetDied">Previously engaged entity is confirmed dead.</param>
    /// <param name="isHoldingUntilDead">Sticky latch: engaged in combat and waiting for death.</param>
    /// <param name="standStillPunisherActive">Pyretic-style debuff — autos must stop.</param>
    public static bool? GetDesiredState(
        bool managementEnabled,
        bool pluginEnabled,
        bool playerAlive,
        bool inCombat,
        bool currentlyAutoAttacking,
        bool hasLivingHostileTarget,
        bool targetIsDead,
        bool engagedTargetStillAlive,
        bool engagedTargetDied,
        bool isHoldingUntilDead,
        bool standStillPunisherActive)
    {
        if (!managementEnabled || !pluginEnabled || !playerAlive)
            return null;

        // Auto-attacks trigger pyretic; force off and do not re-enable.
        if (standStillPunisherActive)
            return false;

        // Confirmed death: stop.
        if (targetIsDead || engagedTargetDied)
            return false;

        // Living hard target while holding / in combat / already AA: keep or start AA.
        if (hasLivingHostileTarget && (isHoldingUntilDead || inCombat || currentlyAutoAttacking))
            return true;

        // Soft hold: engaged target still alive but hard target dropped (gaze, etc.).
        // Do not force AA on without a hard target; do not force off either.
        if (isHoldingUntilDead && engagedTargetStillAlive)
            return null;

        // Out of combat, nothing to finish: clean up stray AA.
        if (!inCombat && !isHoldingUntilDead && currentlyAutoAttacking && !hasLivingHostileTarget)
            return false;

        return null;
    }

    /// <summary>
    /// Whether the sticky hold latch should become (or stay) active this frame.
    /// </summary>
    public static bool ShouldHold(
        bool currentlyHolding,
        bool inCombat,
        bool hasLivingHostileTarget,
        bool engagedTargetStillAlive,
        bool targetIsDead,
        bool engagedTargetDied,
        bool standStillPunisherActive,
        bool pluginEnabled,
        bool playerAlive)
    {
        if (!pluginEnabled || !playerAlive || standStillPunisherActive)
            return false;

        if (targetIsDead || engagedTargetDied)
            return false;

        if (hasLivingHostileTarget && inCombat)
            return true;

        // Keep holding while the engaged enemy is still alive even if InCombat or hard-target flickers.
        return currentlyHolding && (hasLivingHostileTarget || engagedTargetStillAlive);
    }

    /// <summary>
    /// Whether rotations should treat this frame as in-combat because we are finishing a living target.
    /// </summary>
    public static bool ShouldTreatAsInCombat(bool managementEnabled, bool isHoldingUntilDead, bool hasLivingHostileTarget) =>
        managementEnabled && isHoldingUntilDead && hasLivingHostileTarget;
}
