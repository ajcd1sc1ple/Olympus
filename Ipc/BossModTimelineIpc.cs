using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using Dalamud.Plugin.Services;

namespace Olympus.Ipc;

/// <summary>
/// Dalamud IPC subscriber for BossMod / BossMod Reborn timeline + cast-hint channels.
/// Merge prefers cast-hint when present, otherwise Timeline — one answer per mechanic.
/// </summary>
public sealed class BossModTimelineIpc : IBossModTimelineIpc, IDisposable
{
    /// <summary>BossMod returns this when no matching transition exists.</summary>
    private const float NoneSentinel = float.MaxValue;

    /// <summary>Ignore absurd far-future values from clock skew.</summary>
    private const float MaxUsefulSeconds = 600f;

    /// <summary>BossMod AIHints.PredictedDamageType.Shared</summary>
    private const int PredictedDamageTypeShared = 3;

    private readonly IPluginLog _log;
    private readonly ICallGateSubscriber<bool>? _hasActiveModule;
    private readonly ICallGateSubscriber<string?>? _activeModuleName;
    private readonly ICallGateSubscriber<float>? _nextRaidwideIn;
    private readonly ICallGateSubscriber<float>? _nextTankbusterIn;
    private readonly ICallGateSubscriber<float>? _nextRaidwideDamageIn;
    private readonly ICallGateSubscriber<float>? _nextTankbusterDamageIn;
    private readonly ICallGateSubscriber<float>? _nextDamageIn;
    private readonly ICallGateSubscriber<int>? _nextDamageType;
    private readonly ICallGateSubscriber<float>? _nextDowntimeIn;
    private bool _loggedUnavailable;

    public BossModTimelineIpc(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        _log = log;

        try
        {
            _hasActiveModule = pluginInterface.GetIpcSubscriber<bool>("BossMod.HasActiveModule");
            _activeModuleName = pluginInterface.GetIpcSubscriber<string?>("BossMod.ActiveModuleName");
            _nextRaidwideIn = pluginInterface.GetIpcSubscriber<float>("BossMod.Timeline.NextRaidwideIn");
            _nextTankbusterIn = pluginInterface.GetIpcSubscriber<float>("BossMod.Timeline.NextTankbusterIn");
            _nextRaidwideDamageIn = pluginInterface.GetIpcSubscriber<float>("BossMod.Hints.NextRaidwideDamageIn");
            _nextTankbusterDamageIn = pluginInterface.GetIpcSubscriber<float>("BossMod.Hints.NextTankbusterDamageIn");
            // Stack markers use PredictedDamageType.Shared; BossMod exposes the soonest
            // prediction via NextDamageIn/Type (no dedicated NextSharedDamageIn channel).
            _nextDamageIn = pluginInterface.GetIpcSubscriber<float>("BossMod.Hints.NextDamageIn");
            _nextDamageType = pluginInterface.GetIpcSubscriber<int>("BossMod.Hints.NextDamageType");
            _nextDowntimeIn = pluginInterface.GetIpcSubscriber<float>("BossMod.Timeline.NextDowntimeIn");
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[BossModTimelineIpc] Failed to create IPC subscribers");
        }
    }

    public bool Available
    {
        get
        {
            if (_hasActiveModule is null)
                return false;

            try
            {
                _ = _hasActiveModule.InvokeFunc();
                _loggedUnavailable = false;
                return true;
            }
            catch (IpcNotReadyError)
            {
                return false;
            }
            catch (Exception ex)
            {
                if (!_loggedUnavailable)
                {
                    _log.Debug(ex, "[BossModTimelineIpc] BossMod timeline IPC unavailable");
                    _loggedUnavailable = true;
                }

                return false;
            }
        }
    }

    public bool HasActiveModule() => TryInvoke(_hasActiveModule, false);

    public string? ActiveModuleName() => TryInvoke(_activeModuleName, null);

    public float? NextRaidwideIn() => Normalize(TryInvoke(_nextRaidwideIn, NoneSentinel));

    public float? NextTankbusterIn() => Normalize(TryInvoke(_nextTankbusterIn, NoneSentinel));

    public float? NextRaidwideDamageIn() => Normalize(TryInvoke(_nextRaidwideDamageIn, NoneSentinel));

    public float? NextTankbusterDamageIn() => Normalize(TryInvoke(_nextTankbusterDamageIn, NoneSentinel));

    public float? NextSharedDamageIn()
    {
        // Prefer Shared when it is the soonest PredictedDamage entry. Spreads/bait AoEs
        // use Raidwide and must not drive GCD shield prep (Anthracite AnthrabombSpread).
        var type = TryInvoke(_nextDamageType, 0);
        if (type != PredictedDamageTypeShared)
            return null;
        return Normalize(TryInvoke(_nextDamageIn, NoneSentinel));
    }

    public float? NextDowntimeIn() => Normalize(TryInvoke(_nextDowntimeIn, NoneSentinel));

    private static float? Normalize(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            return null;
        if (value <= 0f || value >= MaxUsefulSeconds || value >= NoneSentinel / 2f)
            return null;
        return value;
    }

    private static T TryInvoke<T>(ICallGateSubscriber<T>? gate, T fallback)
    {
        if (gate is null)
            return fallback;

        try
        {
            return gate.InvokeFunc();
        }
        catch (IpcNotReadyError)
        {
            return fallback;
        }
        catch
        {
            return fallback;
        }
    }

    public void Dispose()
    {
        // CallGate subscribers do not require disposal.
    }
}
