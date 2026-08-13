using System;
using System.Collections.Generic;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace Olympus.Services.Movement;

/// <summary>
/// Detects loaded BossMod / BossMod Reborn so Olympus can avoid fighting their movement AI.
/// </summary>
public interface IBossModPresence
{
    /// <summary>True when a BossMod family plugin is currently loaded.</summary>
    bool IsLoaded { get; }

    /// <summary>Display name of the detected plugin, or null if none.</summary>
    string? DetectedName { get; }

    /// <summary>Refresh the cached presence (safe to call each frame; throttled internally).</summary>
    void Refresh();
}

/// <inheritdoc />
public sealed class BossModPresence : IBossModPresence
{
    private static readonly HashSet<string> KnownInternalNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "BossMod",
        "BossModReborn",
    };

    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly IPluginLog? _log;
    private DateTime _nextRefreshUtc = DateTime.MinValue;
    private bool _isLoaded;
    private string? _detectedName;

    public BossModPresence(IDalamudPluginInterface pluginInterface, IPluginLog? log = null)
    {
        _pluginInterface = pluginInterface;
        _log = log;
        Refresh(force: true);
    }

    public bool IsLoaded => _isLoaded;

    public string? DetectedName => _detectedName;

    public void Refresh() => Refresh(force: false);

    private void Refresh(bool force)
    {
        var now = DateTime.UtcNow;
        if (!force && now < _nextRefreshUtc)
            return;

        _nextRefreshUtc = now.AddSeconds(2);

        try
        {
            foreach (var plugin in _pluginInterface.InstalledPlugins)
            {
                if (!plugin.IsLoaded)
                    continue;

                if (!KnownInternalNames.Contains(plugin.InternalName))
                    continue;

                _isLoaded = true;
                _detectedName = string.IsNullOrEmpty(plugin.Name) ? plugin.InternalName : plugin.Name;
                return;
            }

            _isLoaded = false;
            _detectedName = null;
        }
        catch (Exception ex)
        {
            _log?.Debug(ex, "[BossModPresence] Failed to scan InstalledPlugins");
            _isLoaded = false;
            _detectedName = null;
        }
    }
}
