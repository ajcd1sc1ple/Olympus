namespace Olympus.Ipc;

/// <summary>
/// Decides whether modules should treat the player as moving for GCD selection
/// (prefer instant fillers / skip cast-time GCDs).
/// <para>
/// Olympus feeds this into <c>context.IsMoving</c>. That flag drives filler vs hardcast
/// branches — it is not the same as WrathCombo's "allow UseAction while CanOrbwalk".
/// Treating Orbwalker-active pathing as stationary skips fillers and only queues hardcasts
/// that cancel for the whole BossMod reposition (long no-cast pauses).
/// </para>
/// <para>
/// Hardcasts are allowed while physically moving only once Orbwalker has actually locked
/// movement (casting / Buffer DelayedAction / combat force-stop). Until then, keep reporting
/// moving so instant fillers continue to cast.
/// </para>
/// </summary>
public static class OrbwalkerCastGate
{
    /// <summary>
    /// Returns true when modules should prefer movement fillers / suppress hardcasts.
    /// </summary>
    public static bool ShouldBlockHardcasts(
        bool isMoving,
        bool integrationEnabled,
        bool orbwalkerActiveForJob,
        bool orbwalkerMovementLocked = false)
    {
        if (!isMoving)
            return false;

        // Orbwalker has stopped the player for a cast/buffer — allow hardcasts.
        if (integrationEnabled && orbwalkerActiveForJob && orbwalkerMovementLocked)
            return false;

        return true;
    }
}
