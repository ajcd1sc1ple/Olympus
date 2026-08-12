namespace Olympus.Ipc;

/// <summary>
/// Pure helper deciding whether movement should suppress cast-time GCDs.
/// Hardcasts are only allowed while "moving" when Orbwalker has already locked movement —
/// otherwise Olympus starts a cast mid-slide and the game cancels it (recast stutter).
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
        bool orbwalkerMovementLocked)
    {
        if (!isMoving)
            return false;

        // Only trust Orbwalker once it is actively holding the character still.
        // Allowing hardcasts merely because Orbwalker is installed races its lock window
        // (Olympus queues earlier than Orbwalker's ~0.1s GCD cutoff) and causes cancel loops.
        if (integrationEnabled && orbwalkerActiveForJob && orbwalkerMovementLocked)
            return false;

        return true;
    }
}
