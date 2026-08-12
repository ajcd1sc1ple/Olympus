using System.Collections.Generic;

namespace Olympus.Ipc;

/// <summary>
/// Consumer for PunishXIV Orbwalker's EzIPC surface.
/// Failures (plugin missing, IPC not ready) are treated as offline.
/// </summary>
public interface IOrbwalkerIpc
{
    /// <summary>
    /// True when Orbwalker IPC subscribers are present and at least one call succeeds.
    /// </summary>
    bool Available { get; }

    /// <summary>
    /// Orbwalker's master enable toggle.
    /// </summary>
    bool PluginEnabled();

    /// <summary>
    /// True while Orbwalker is currently blocking movement input.
    /// </summary>
    bool MovementLocked();

    /// <summary>
    /// True when Orbwalker combat force-stop / slidecast mode is enabled
    /// (<c>ForceStopMoveCombat</c>).
    /// </summary>
    bool OrbwalkingMode();

    /// <summary>
    /// Job IDs for which Orbwalker is enabled.
    /// </summary>
    IReadOnlyList<uint> EnabledJobs();

    /// <summary>
    /// True when Orbwalker is installed, plugin-enabled, and enabled for <paramref name="jobId"/>.
    /// </summary>
    bool IsActiveForJob(uint jobId);
}
