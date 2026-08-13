namespace Olympus.Ipc;

/// <summary>
/// BossMod / BossMod Reborn timeline IPC.
/// Prefixed as <c>BossMod.*</c> by BossMod's IPC provider.
/// Raidwide and tankbuster each use a single Timeline channel — no Hints dual-source.
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
    /// Seconds until next raidwide from BossMod's Timeline API.
    /// Returns null when unavailable / none scheduled.
    /// </summary>
    float? NextRaidwideIn();

    /// <summary>
    /// Seconds until next tankbuster from BossMod's Timeline API.
    /// Returns null when unavailable / none scheduled.
    /// </summary>
    float? NextTankbusterIn();

    /// <summary>
    /// Seconds until next downtime start (often untargetable).
    /// Returns null when unavailable / none scheduled.
    /// </summary>
    /// <remarks>
    /// Exposed for future use. Not wired into <c>SecondsUntilNextUntargetablePhase</c>
    /// because BossMod DowntimeStart is broader than Cactbot untargetable markers.
    /// </remarks>
    float? NextDowntimeIn();
}
