using Olympus.Config;

namespace Olympus.Rotation.Common.Modules;

/// <summary>
/// Maps <see cref="RaiseExecutionMode"/> to raise GCD priority and party-stability gates.
/// Lower scheduler priority wins.
/// </summary>
public static class RaiseModeEvaluator
{
    /// <summary>Balanced: skip raise GCD while someone is critically low.</summary>
    public const float BalancedCriticalLowestHp = 0.40f;

    /// <summary>HealFirst: require average party HP at or above this.</summary>
    public const float HealFirstMinAvgHp = 0.70f;

    /// <summary>HealFirst: require lowest living member HP at or above this.</summary>
    public const float HealFirstMinLowestHp = 0.50f;

    /// <summary>
    /// Decide whether to push a raise GCD and at what priority.
    /// </summary>
    /// <param name="mode">Configured raise priority mode.</param>
    /// <param name="avgHpPercent">Living party average HP (0–1).</param>
    /// <param name="lowestHpPercent">Living party lowest HP (0–1).</param>
    /// <param name="priority">Scheduler priority when allowed (lower = higher urgency).</param>
    /// <param name="skipReason">Human-readable reason when skipped; null when allowed.</param>
    /// <returns>True when the raise GCD should be pushed.</returns>
    public static bool TryGetRaiseGcdPriority(
        RaiseExecutionMode mode,
        float avgHpPercent,
        float lowestHpPercent,
        out int priority,
        out string? skipReason)
    {
        switch (mode)
        {
            case RaiseExecutionMode.RaiseFirst:
                priority = 0;
                skipReason = null;
                return true;

            case RaiseExecutionMode.Balanced:
                if (lowestHpPercent < BalancedCriticalLowestHp)
                {
                    priority = 0;
                    skipReason = $"Party critical ({lowestHpPercent:P0}) — heal first";
                    return false;
                }

                priority = 1;
                skipReason = null;
                return true;

            case RaiseExecutionMode.HealFirst:
                if (avgHpPercent < HealFirstMinAvgHp || lowestHpPercent < HealFirstMinLowestHp)
                {
                    priority = 0;
                    skipReason = $"Party unstable (avg {avgHpPercent:P0}, low {lowestHpPercent:P0})";
                    return false;
                }

                // After emergency heals (Bene ~10, Assize ~15) but before routine GCD heals.
                priority = 18;
                skipReason = null;
                return true;

            default:
                priority = 1;
                skipReason = null;
                return true;
        }
    }

    /// <summary>
    /// oGCD prep (Swiftcast / Lightspeed) priority. Always allowed when a corpse exists;
    /// RaiseFirst gets slightly higher urgency so the instant buff wins the weave.
    /// </summary>
    public static int GetRaisePrepPriority(RaiseExecutionMode mode)
        => mode == RaiseExecutionMode.RaiseFirst ? 0 : 1;
}
