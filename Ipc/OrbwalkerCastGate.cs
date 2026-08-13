namespace Olympus.Ipc;

/// <summary>
/// Pure helper deciding whether movement should suppress cast-time GCDs.
/// Mirrors WrathCombo Auto-Rotation: when Orbwalker integration is active for the job,
/// hardcasts are allowed while moving — Orbwalker locks (and optionally buffers) the cast.
/// Waiting for <c>MovementLocked</c> first races Orbwalker's lock window and causes
/// cancel → recast spam every frame.
/// </summary>
public static class OrbwalkerCastGate
{
    /// <summary>
    /// Returns true when Olympus should treat the player as moving for hardcast selection
    /// (prefer instant fillers / skip cast-time GCDs).
    /// </summary>
    public static bool ShouldBlockHardcasts(
        bool isMoving,
        bool integrationEnabled,
        bool orbwalkerActiveForJob,
        bool orbwalkerMovementLocked = false)
    {
        if (!isMoving)
            return false;

        // WrathCombo: orbwalking = OrbwalkerIntegration && CanOrbwalk.
        // MovementLocked is optional — Orbwalker locks once the cast/queue starts (or via Buffer).
        if (integrationEnabled && orbwalkerActiveForJob)
            return false;

        _ = orbwalkerMovementLocked; // retained for call-site compatibility / diagnostics
        return true;
    }
}
