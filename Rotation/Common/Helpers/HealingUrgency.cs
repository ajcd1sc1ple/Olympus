using Olympus.Config;

namespace Olympus.Rotation.Common.Helpers;

/// <summary>
/// Shared healer GCD urgency helpers.
/// <see cref="HealingConfig.GcdEmergencyThreshold"/> is documented as
/// "interrupt DPS and heal immediately" but was never consulted by healer
/// DamageModules — DPS candidates always filled the GCD whenever heal handlers
/// skipped (shields, co-healer defer, job thresholds, movement).
/// </summary>
internal static class HealingUrgency
{
    /// <summary>
    /// True when the heal master toggle is on and the lowest living party
    /// member is at or below the configured GCD emergency HP fraction.
    /// Callers should skip pushing damage GCDs so a heal can own the GCD.
    /// </summary>
    public static bool ShouldSuppressDamageGcds(
        bool healingEnabled,
        float lowestHpPercent,
        HealingConfig healing)
    {
        if (!healingEnabled)
            return false;

        return lowestHpPercent <= healing.GcdEmergencyThreshold;
    }

    /// <summary>
    /// True when <paramref name="hpPercent"/> is at or below the GCD emergency
    /// threshold (heal handlers should force a ST heal, ignoring job thresholds
    /// / shield-skip / co-healer deferral).
    /// </summary>
    public static bool IsEmergencyHp(float hpPercent, HealingConfig healing)
        => hpPercent <= healing.GcdEmergencyThreshold;
}
