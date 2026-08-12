namespace Olympus.Ipc;

/// <summary>
/// Pure helper deciding whether movement should suppress cast-time GCDs.
/// When Orbwalker covers the current job, hardcasts are allowed while move keys are held.
/// </summary>
public static class OrbwalkerCastGate
{
    /// <summary>
    /// Returns true when Olympus should treat the player as moving for hardcast selection
    /// (prefer instant fillers / skip cast-time GCDs).
    /// </summary>
    public static bool ShouldBlockHardcasts(bool isMoving, bool integrationEnabled, bool orbwalkerActiveForJob)
    {
        if (!isMoving)
            return false;

        if (integrationEnabled && orbwalkerActiveForJob)
            return false;

        return true;
    }
}
