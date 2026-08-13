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
    /// If more than this many seconds remained on the cast bar when IsCasting cleared,
    /// treat the falling edge as a move-cancel (not a slidecast / natural finish).
    /// </summary>
    public const float SlidecastRemainingSeconds = 0.55f;

    /// <summary>
    /// Arms or preserves the hold deadline from a mid-cast cancel falling edge.
    /// Clears immediately once the player is stationary so hardcasts resume after a dodge.
    /// Successful cast completions / slidecasts do not arm the hold.
    /// </summary>
    public static DateTime UpdateHoldUntil(
        bool wasCastingCastTimeGcd,
        bool isCasting,
        bool isMoving,
        bool castWasCancelled,
        DateTime now,
        TimeSpan holdDuration,
        DateTime currentHoldUntil)
    {
        // Stopped → resume hardcasts immediately (do not keep the cancel hold armed).
        if (!isMoving)
            return DateTime.MinValue;

        // Falling edge of a cast-time GCD that died mid-cast while still moving.
        if (wasCastingCastTimeGcd && !isCasting && castWasCancelled)
            return now + holdDuration;

        return currentHoldUntil;
    }

    /// <summary>
    /// True when the previous frame's cast bar still had more than a slidecast window left,
    /// so IsCasting clearing means interrupt/cancel rather than finish.
    /// </summary>
    public static bool WasCancelled(float previousCurrentCastTime, float previousTotalCastTime)
    {
        if (previousTotalCastTime <= 0f)
            return false;

        var remaining = previousTotalCastTime - previousCurrentCastTime;
        return remaining > SlidecastRemainingSeconds;
    }

    /// <summary>True while hardcasts should stay suppressed after a cancel.</summary>
    public static bool ShouldBlock(DateTime now, DateTime holdUntil) => now < holdUntil;

    /// <summary>
    /// Combines the post-cancel hold with Orbwalker coverage.
    /// When Orbwalker is active for the job (WrathCombo CanOrbwalk) — or already movement-locked —
    /// the hold must not win: otherwise hardcasts stay suppressed, Orbwalker never re-locks,
    /// and BossMod pathing deadlocks casting. ActionService submit cooldown still anti-spams.
    /// </summary>
    public static bool ShouldBlockHardcasts(
        bool holdActive,
        bool orbwalkerMovementLocked,
        bool orbwalkerActiveForJob = false) =>
        holdActive && !orbwalkerMovementLocked && !orbwalkerActiveForJob;

    public static float ClampHoldSeconds(float seconds) =>
        Math.Clamp(seconds, MinHoldSeconds, MaxHoldSeconds);
}
