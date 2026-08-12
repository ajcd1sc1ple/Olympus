using System.Numerics;

namespace Olympus.Rotation.Common.Helpers;

/// <summary>
/// Position-based movement gate for hardcast selection.
/// Thresholds are intentionally above animation / BossMod pathing micro-jitter so a cancelled
/// cast does not leave Olympus stuck treating the player as permanently moving.
/// </summary>
public static class MovementGate
{
    /// <summary>~0.15 yalms. Ignores standing jitter and tiny AI corrections.</summary>
    public const float DefaultThresholdSquared = 0.0225f;

    /// <summary>~0.30 yalms. More forgiving when BossMod AI is continuously pathing.</summary>
    public const float BossModThresholdSquared = 0.09f;

    public static float ThresholdSquaredFor(bool bossModLoaded) =>
        bossModLoaded ? BossModThresholdSquared : DefaultThresholdSquared;

    public static bool HasMoved(Vector3 current, Vector3 previous, float thresholdSquared) =>
        Vector3.DistanceSquared(current, previous) > thresholdSquared;

    /// <summary>
    /// True while position is changing or we are still inside the post-stop grace window.
    /// </summary>
    public static bool IsMoving(
        bool positionChanged,
        double secondsSinceLastMovement,
        float movementToleranceSeconds) =>
        positionChanged || secondsSinceLastMovement < movementToleranceSeconds;
}
