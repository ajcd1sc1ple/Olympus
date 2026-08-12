using System;

namespace Olympus.Rotation.Common.Helpers;

/// <summary>
/// After a cast-time GCD is cancelled while moving, briefly keep treating the player as
/// moving for hardcast selection so Olympus does not immediately spam-retry the cast.
/// </summary>
public static class PostCancelCastHold
{
    public const float DefaultHoldSeconds = 0.4f;
    public const float MinHoldSeconds = 0.3f;
    public const float MaxHoldSeconds = 0.6f;

    /// <summary>
    /// Arms or preserves the hold deadline from a casting falling edge.
    /// </summary>
    public static DateTime UpdateHoldUntil(
        bool wasCastingCastTimeGcd,
        bool isCasting,
        bool isMoving,
        DateTime now,
        TimeSpan holdDuration,
        DateTime currentHoldUntil)
    {
        // Falling edge of a cast-time GCD while still moving → cancel/stutter risk.
        if (wasCastingCastTimeGcd && !isCasting && isMoving)
            return now + holdDuration;

        return currentHoldUntil;
    }

    /// <summary>True while hardcasts should stay suppressed after a cancel.</summary>
    public static bool ShouldBlock(DateTime now, DateTime holdUntil) => now < holdUntil;

    public static float ClampHoldSeconds(float seconds) =>
        Math.Clamp(seconds, MinHoldSeconds, MaxHoldSeconds);
}
