using System;
using System.Collections.Generic;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using Dalamud.Plugin.Services;

namespace Olympus.Ipc;

/// <summary>
/// Dalamud IPC subscriber for PunishXIV Orbwalker (InternalName: Orbwalker).
/// Channel names match EzIPC defaults: <c>Orbwalker.{MethodName}</c>.
/// </summary>
public sealed class OrbwalkerIpc : IOrbwalkerIpc, IDisposable
{
    private readonly IPluginLog _log;
    private readonly ICallGateSubscriber<bool>? _pluginEnabled;
    private readonly ICallGateSubscriber<bool>? _movementLocked;
    private readonly ICallGateSubscriber<bool>? _orbwalkingMode;
    private readonly ICallGateSubscriber<List<uint>>? _enabledJobs;
    private bool _loggedUnavailable;

    public OrbwalkerIpc(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        _log = log;

        try
        {
            _pluginEnabled = pluginInterface.GetIpcSubscriber<bool>("Orbwalker.PluginEnabled");
            _movementLocked = pluginInterface.GetIpcSubscriber<bool>("Orbwalker.MovementLocked");
            _orbwalkingMode = pluginInterface.GetIpcSubscriber<bool>("Orbwalker.OrbwalkingMode");
            _enabledJobs = pluginInterface.GetIpcSubscriber<List<uint>>("Orbwalker.EnabledJobs");
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[OrbwalkerIpc] Failed to create IPC subscribers");
        }
    }

    public bool Available
    {
        get
        {
            if (_pluginEnabled is null)
                return false;

            try
            {
                _ = _pluginEnabled.InvokeFunc();
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
                    _log.Debug(ex, "[OrbwalkerIpc] Orbwalker IPC unavailable");
                    _loggedUnavailable = true;
                }

                return false;
            }
        }
    }

    public bool PluginEnabled() => TryInvoke(_pluginEnabled, false);

    public bool MovementLocked() => TryInvoke(_movementLocked, false);

    public bool OrbwalkingMode() => TryInvoke(_orbwalkingMode, false);

    public IReadOnlyList<uint> EnabledJobs()
    {
        if (_enabledJobs is null)
            return Array.Empty<uint>();

        try
        {
            var jobs = _enabledJobs.InvokeFunc();
            return jobs is { Count: > 0 } ? jobs : Array.Empty<uint>();
        }
        catch (IpcNotReadyError)
        {
            return Array.Empty<uint>();
        }
        catch
        {
            return Array.Empty<uint>();
        }
    }

    public bool IsActiveForJob(uint jobId)
    {
        if (!Available || !PluginEnabled())
            return false;

        var jobs = EnabledJobs();
        for (var i = 0; i < jobs.Count; i++)
        {
            if (jobs[i] == jobId)
                return true;
        }

        return false;
    }

    public void Dispose()
    {
        // CallGate subscribers do not require explicit disposal.
    }

    private static T TryInvoke<T>(ICallGateSubscriber<T>? subscriber, T fallback)
    {
        if (subscriber is null)
            return fallback;

        try
        {
            return subscriber.InvokeFunc();
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
}
