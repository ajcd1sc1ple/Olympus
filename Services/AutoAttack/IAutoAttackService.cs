namespace Olympus.Services.AutoAttack;

/// <summary>
/// Keeps game auto-attack enabled until the engaged enemy dies, then turns it off.
/// </summary>
public interface IAutoAttackService
{
    /// <summary>
    /// True while we are sticking to a living enemy engaged in combat (until death).
    /// </summary>
    bool IsHoldingUntilDead { get; }

    /// <summary>
    /// Entity id of the enemy we are holding on, or 0 when not holding.
    /// </summary>
    ulong EngagedTargetId { get; }

    /// <summary>
    /// True when management is on and we still have a living hostile hard target under hold —
    /// rotations should keep executing even if the server InCombat flag flickered off.
    /// </summary>
    bool ShouldTreatAsInCombat { get; }

    /// <summary>
    /// Frame update: latch engagement and sync auto-attack state.
    /// </summary>
    void Update(bool inCombat);
}
