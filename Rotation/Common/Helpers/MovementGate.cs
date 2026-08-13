using System;
using System.Numerics;

namespace Olympus.Rotation.Common.Helpers;

/// <summary>
/// Movement gate for hardcast selection.
/// Uses horizontal (XZ) speed so animation Y-bob and BossMod arrive-threshold crawl
/// (~0.1y) do not permanently starve cast-time GCDs. BossMod AI only pauses pathing
/// for an in-progress cast; if Olympus never starts one, AI keeps micro-pathing and
/// a position-delta latch deadlocks hardcasts until the player taps WASD.
/// </summary>
public static class MovementGate
{
    /// <summary>~0.15 yalms. Ignores standing jitter and tiny AI corrections.</summary>
    public const float DefaultThresholdSquared = 0.0225f;

    /// <summary>~0.30 yalms. More forgiving when BossMod AI is continuously pathing.</summary>
    public const float BossModThresholdSquared = 0.09f;

    /// <summary>
    /// Horizontal speed (yalms/sec) above which the player counts as moving for hardcasts.
    /// Below walk speed so real strafes still gate, above BossMod arrive crawl.
    /// </summary>
    public const float DefaultSpeedThreshold = 1.25f;

    /// <summary>
    /// Higher speed floor when BossMod is loaded so max-melee / follow micro-pathing
    /// does not keep hardcasts blocked between real dodges.
    /// </summary>
    public const float BossModSpeedThreshold = 2.25f;

    /// <summary>EMA blend for smoothed speed (higher = snappier).</summary>
    public const float SpeedSmoothing = 0.35f;

    public static float ThresholdSquaredFor(bool bossModLoaded) =>
        bossModLoaded ? BossModThresholdSquared : DefaultThresholdSquared;

    public static float SpeedThresholdFor(bool bossModLoaded) =>
        bossModLoaded ? BossModSpeedThreshold : DefaultSpeedThreshold;

    /// <summary>Horizontal (XZ) distance squared — ignores vertical bob / knockup recovery.</summary>
    public static float HorizontalDistanceSquared(Vector3 current, Vector3 previous)
    {
        var dx = current.X - previous.X;
        var dz = current.Z - previous.Z;
        return dx * dx + dz * dz;
    }

    public static bool HasMoved(Vector3 current, Vector3 previous, float thresholdSquared) =>
        HorizontalDistanceSquared(current, previous) > thresholdSquared;

    /// <summary>
    /// Instantaneous horizontal speed from a frame delta. Clamps dt to avoid spikes on stalls.
    /// </summary>
    public static float HorizontalSpeed(Vector3 current, Vector3 previous, float deltaSeconds)
    {
        var dt = Math.Clamp(deltaSeconds, 1f / 240f, 0.25f);
        return MathF.Sqrt(HorizontalDistanceSquared(current, previous)) / dt;
    }

    /// <summary>Exponential moving average of speed samples.</summary>
    public static float SmoothSpeed(float previousSmoothed, float sample, float blend = SpeedSmoothing)
    {
        blend = Math.Clamp(blend, 0f, 1f);
        return previousSmoothed + (sample - previousSmoothed) * blend;
    }

    /// <summary>
    /// True while smoothed speed exceeds the threshold or we are still inside the post-stop grace window.
    /// </summary>
    public static bool IsMoving(
        float smoothedSpeed,
        float speedThreshold,
        double secondsSinceLastMovement,
        float movementToleranceSeconds) =>
        smoothedSpeed > speedThreshold || secondsSinceLastMovement < movementToleranceSeconds;

    /// <summary>
    /// Legacy position-delta helper retained for tests / callers that only have a bool edge.
    /// Prefer the speed overload for hardcast gating.
    /// </summary>
    public static bool IsMoving(
        bool positionChanged,
        double secondsSinceLastMovement,
        float movementToleranceSeconds) =>
        positionChanged || secondsSinceLastMovement < movementToleranceSeconds;
}
