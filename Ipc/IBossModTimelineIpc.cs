namespace Olympus.Ipc;

/// <summary>
/// BossMod / BossMod Reborn timeline + hint IPC (RSR-compatible channel names).
/// Prefixed as <c>BossMod.*</c> by BossMod's IPC provider.
/// </summary>
public interface IBossModTimelineIpc
{
    /// <summary>True when BossMod IPC responds.</summary>
    bool Available { get; }

    /// <summary>True when an encounter module is actively running.</summary>
    bool HasActiveModule();

    /// <summary>Primary actor / module display name, or null.</summary>
    string? ActiveModuleName();

    /// <summary>
    /// Seconds until next raidwide state transition.
    /// Returns null when unavailable / none scheduled.
    /// </summary>
    float? NextRaidwideIn();

    /// <summary>
    /// Seconds until next tankbuster state transition.
    /// Returns null when unavailable / none scheduled.
    /// </summary>
    float? NextTankbusterIn();

    /// <summary>
    /// Seconds until next predicted raidwide damage from AI hints (cast-based).
    /// Returns null when unavailable / none scheduled.
    /// </summary>
    float? NextRaidwideDamageIn();

    /// <summary>
    /// Seconds until next predicted tankbuster damage from AI hints (cast-based).
    /// Returns null when unavailable / none scheduled.
    /// </summary>
    float? NextTankbusterDamageIn();

    /// <summary>
    /// Seconds until next downtime start (often untargetable).
    /// Returns null when unavailable / none scheduled.
    /// </summary>
    float? NextDowntimeIn();
}
