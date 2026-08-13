using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Dalamud.Plugin.Services;
using Olympus.Ipc;
using Olympus.Services;
using Olympus.Timeline.Models;
using Olympus.Timeline.Parser;

namespace Olympus.Timeline;

/// <summary>
/// Runtime service for fight timeline tracking and mechanic prediction.
/// Maintains timeline state, syncs to game events, and provides predictions to rotation modules.
/// Merges embedded Cactbot timelines with BossMod Reborn Timeline IPC when available
/// (single Timeline channel per mechanic — not Hints).
/// </summary>
public sealed class TimelineService : ITimelineService, IDisposable
{
    private readonly IPluginLog log;
    private readonly ICombatEventService combatEventService;
    private readonly Configuration configuration;
    private readonly CactbotTimelineParser parser;
    private IBossModTimelineIpc? bossModTimeline;

    private FightTimeline? loadedTimeline;
    private TimelineState? state;

    // Prediction cache to avoid allocations in Update() hot path
    private MechanicPrediction? cachedNextRaidwide;
    private MechanicPrediction? cachedNextTankBuster;
    private float lastPredictionUpdateTime;
    private DateTime lastBossModOnlyRefreshUtc = DateTime.MinValue;
    private const float PredictionCacheRefreshInterval = 0.25f; // Refresh predictions every 250ms

    // Simulation state
    private bool isSimulating;
    private float simulationStartTime;
    private DateTime simulationStartRealTime;

    public TimelineService(
        IPluginLog log,
        ICombatEventService combatEventService,
        Configuration configuration,
        IBossModTimelineIpc? bossModTimeline = null)
    {
        this.log = log;
        this.combatEventService = combatEventService;
        this.configuration = configuration;
        this.bossModTimeline = bossModTimeline;
        this.parser = new CactbotTimelineParser();
    }

    /// <summary>Optional late bind when IPC is constructed after this service.</summary>
    public void AttachBossModTimeline(IBossModTimelineIpc? ipc) => bossModTimeline = ipc;

    #region ITimelineService Properties

    public bool IsActive
    {
        get
        {
            if (isSimulating)
                return loadedTimeline != null && state != null;

            if (!combatEventService.IsInCombat)
                return false;

            if (loadedTimeline != null && state != null)
                return true;

            return IsBossModTimelineLive();
        }
    }

    public bool IsSimulating => isSimulating;

    public float CurrentTime => state?.CurrentTime ?? 0f;

    public string CurrentPhase => state?.CurrentPhase ?? string.Empty;

    public string FightName
    {
        get
        {
            if (!string.IsNullOrEmpty(loadedTimeline?.Name))
                return loadedTimeline!.Name;

            if (IsBossModTimelineLive())
                return bossModTimeline?.ActiveModuleName() ?? "BossMod";

            return string.Empty;
        }
    }

    public float Confidence
    {
        get
        {
            var local = state?.Confidence ?? 0f;
            if (IsBossModTimelineLive())
                return Math.Max(local, BossModTimelineMerge.BossModConfidence);
            return local;
        }
    }

    public MechanicPrediction? NextRaidwide => IsActive ? cachedNextRaidwide : null;

    public MechanicPrediction? NextTankBuster => IsActive ? cachedNextTankBuster : null;

    #endregion

    #region ITimelineService Methods

    public void Update()
    {
        // BossMod-only path: no embedded Cactbot timeline for this zone.
        if (loadedTimeline == null || state == null)
        {
            UpdateBossModOnly();
            return;
        }

        float currentTime;

        if (isSimulating)
        {
            // Simulation mode - use real elapsed time with perfect confidence
            var elapsed = (float)(DateTime.UtcNow - simulationStartRealTime).TotalSeconds;
            currentTime = simulationStartTime + elapsed;
            state.ForceSync(currentTime); // Keep 100% confidence in simulation
        }
        else
        {
            // Gate on combat check before calling GetCombatDurationSeconds (which acquires a lock)
            if (!combatEventService.IsInCombat)
            {
                // Not in combat - reset state if we were tracking
                if (state.HasSynced)
                {
                    state.Reset();
                    ClearPredictionCache();
                }
                return;
            }

            // Update timeline position from combat duration
            currentTime = combatEventService.GetCombatDurationSeconds();
            state.UpdateTime(currentTime);
        }

        // Refresh prediction cache periodically (not every frame)
        if (currentTime - lastPredictionUpdateTime >= PredictionCacheRefreshInterval)
        {
            RefreshPredictionCache();
            lastPredictionUpdateTime = currentTime;
        }
    }

    public bool IsMechanicImminent(TimelineEntryType type, float withinSeconds)
    {
        var prediction = GetNextMechanic(type);
        return prediction.HasValue && prediction.Value.SecondsUntil <= withinSeconds;
    }

    public MechanicPrediction? GetNextMechanic(TimelineEntryType type)
    {
        if (!IsActive)
            return null;

        // Raidwide / TB come from the merged cache (BossMod + Cactbot).
        if (type == TimelineEntryType.Raidwide)
            return cachedNextRaidwide;
        if (type == TimelineEntryType.TankBuster)
            return cachedNextTankBuster;

        if (state == null || loadedTimeline == null)
            return null;

        var currentTime = state.CurrentTime;
        var confidence = state.Confidence;

        // Binary search for first entry at or after current time
        var startIndex = loadedTimeline.FindFirstEntryAtOrAfter(currentTime);

        for (var i = startIndex; i < loadedTimeline.Entries.Length; i++)
        {
            var entry = loadedTimeline.Entries[i];

            if (entry.EntryType == type && !entry.IsHidden)
            {
                var secondsUntil = entry.Timestamp - currentTime;
                return new MechanicPrediction(
                    secondsUntil,
                    entry.EntryType,
                    entry.Name,
                    confidence,
                    entry.Duration);
            }
        }

        return null;
    }

    public float? SecondsUntilNextUntargetablePhase()
    {
        // Use embedded Cactbot "--untargetable--" phase markers only.
        // Do NOT merge BossMod Timeline.NextDowntimeIn here: BossMod's DowntimeStart
        // hint is broader than true untargetable windows and was causing long
        // pre-downtime holds / dumps (8–18s) across many jobs.
        if (state == null || loadedTimeline == null)
            return null;
        if (!combatEventService.IsInCombat && !isSimulating)
            return null;
        return FindSecondsUntilNextUntargetablePhase(loadedTimeline, state.CurrentTime);
    }

    /// <summary>
    /// Scans <paramref name="timeline"/> entries from <paramref name="currentTime"/> forward
    /// for the first Phase entry whose name contains "untargetable" (case-insensitive).
    /// Returns seconds until that entry, or null if no matching entry exists ahead of
    /// <paramref name="currentTime"/>. Hidden entries are skipped (mirrors GetNextMechanic).
    /// Internal to allow direct unit-test calls with a pre-parsed FightTimeline.
    /// </summary>
    internal static float? FindSecondsUntilNextUntargetablePhase(FightTimeline timeline, float currentTime)
    {
        var startIndex = timeline.FindFirstEntryAtOrAfter(currentTime);
        for (var i = startIndex; i < timeline.Entries.Length; i++)
        {
            var entry = timeline.Entries[i];
            if (entry.IsHidden)
                continue;
            if (entry.EntryType != TimelineEntryType.Phase)
                continue;
            // Use Contains("untargetable") -- NOT Contains("targetable") -- because
            // "targetable" is a substring of "untargetable" and would match both markers.
            if (entry.Name.Contains("untargetable", StringComparison.OrdinalIgnoreCase))
                return entry.Timestamp - currentTime;
        }
        return null;
    }

    public void LoadForZone(uint zoneId)
    {
        var zoneInfo = TimelineZoneMapping.GetZoneInfo(zoneId);
        if (zoneInfo == null)
        {
            Clear();
            return;
        }

        var info = zoneInfo.Value;
        log.Info("TimelineService: Loading timeline for {0} (zone {1})", info.Name, zoneId);

        try
        {
            var content = LoadEmbeddedResource(info.ResourceName);
            if (string.IsNullOrEmpty(content))
            {
                log.Warning("TimelineService: Timeline resource not found: {0}", info.ResourceName);
                Clear();
                return;
            }

            var timeline = parser.Parse(content, zoneId, info.ContentId, info.Name);
            if (timeline == null)
            {
                log.Warning("TimelineService: Failed to parse timeline for {0}", info.Name);
                Clear();
                return;
            }

            loadedTimeline = timeline;
            state = new TimelineState(timeline);
            ClearPredictionCache();

            log.Info("TimelineService: Loaded timeline with {0} entries", timeline.Entries.Length);
        }
        catch (Exception ex)
        {
            log.Error(ex, "TimelineService: Error loading timeline for zone {0}", zoneId);
            Clear();
        }
    }

    public void Clear()
    {
        loadedTimeline = null;
        state = null;
        ClearPredictionCache();
    }

    public void OnAbilityUsed(uint sourceId, uint actionId)
    {
        if (state == null || (!combatEventService.IsInCombat && !isSimulating))
            return;

        var combatTime = isSimulating
            ? simulationStartTime + (float)(DateTime.UtcNow - simulationStartRealTime).TotalSeconds
            : combatEventService.GetCombatDurationSeconds();

        if (state.TrySync(actionId, combatTime))
        {
            log.Debug("TimelineService: Synced to action {0:X} at {1:F1}s", actionId, state.CurrentTime);

            // Immediately refresh predictions after sync
            RefreshPredictionCache();
            lastPredictionUpdateTime = combatTime;
        }
    }

    #endregion

    #region Simulation Methods

    public void StartSimulation()
    {
        log.Info("TimelineService: Starting simulation mode");

        // Create a test timeline programmatically
        loadedTimeline = CreateTestTimeline();
        state = new TimelineState(loadedTimeline);

        isSimulating = true;
        simulationStartTime = 0f;
        simulationStartRealTime = DateTime.UtcNow;

        // Force initial sync so confidence starts at 100%
        // Simulation has perfect timing, no drift possible
        state.ForceSync(0f);

        ClearPredictionCache();
        RefreshPredictionCache();

        log.Info("TimelineService: Simulation started with {0} entries", loadedTimeline.Entries.Length);
    }

    public void StopSimulation()
    {
        log.Info("TimelineService: Stopping simulation mode");

        isSimulating = false;
        loadedTimeline = null;
        state = null;
        ClearPredictionCache();
    }

    public void SimulateSyncPoint(uint actionId)
    {
        if (!isSimulating || state == null)
            return;

        var currentTime = simulationStartTime + (float)(DateTime.UtcNow - simulationStartRealTime).TotalSeconds;
        if (state.TrySync(actionId, currentTime))
        {
            log.Debug("TimelineService: Simulation synced to action {0:X} at {1:F1}s", actionId, state.CurrentTime);
            RefreshPredictionCache();
        }
    }

    public void AdvanceSimulationTime(float seconds)
    {
        if (!isSimulating || state == null)
            return;

        // Offset the start time backwards to effectively advance the current time
        simulationStartTime += seconds;

        var newTime = simulationStartTime + (float)(DateTime.UtcNow - simulationStartRealTime).TotalSeconds;

        // Force sync to maintain 100% confidence in simulation
        state.ForceSync(newTime);
        RefreshPredictionCache();

        log.Debug("TimelineService: Advanced simulation to {0:F1}s", newTime);
    }

    public IReadOnlyList<MechanicPrediction> GetUpcomingMechanics(float windowSeconds)
    {
        if (!IsActive)
            return Array.Empty<MechanicPrediction>();

        // BossMod-only (no embedded timeline): surface cached raidwide/TB.
        if (state == null || loadedTimeline == null)
        {
            var bossOnly = new List<MechanicPrediction>(2);
            if (cachedNextRaidwide is { } rw && rw.SecondsUntil <= windowSeconds)
                bossOnly.Add(rw);
            if (cachedNextTankBuster is { } tb && tb.SecondsUntil <= windowSeconds)
                bossOnly.Add(tb);
            bossOnly.Sort((a, b) => a.SecondsUntil.CompareTo(b.SecondsUntil));
            return bossOnly;
        }

        var currentTime = state.CurrentTime;
        var confidence = Confidence;
        var endTime = currentTime + windowSeconds;

        var startIndex = loadedTimeline.FindFirstEntryAtOrAfter(currentTime);
        var results = new List<MechanicPrediction>();

        // Prefer merged cache for RW/TB so BossMod predictions appear in the debug list.
        if (cachedNextRaidwide is { } cachedRw && cachedRw.SecondsUntil <= windowSeconds)
            results.Add(cachedRw);
        if (cachedNextTankBuster is { } cachedTb && cachedTb.SecondsUntil <= windowSeconds)
            results.Add(cachedTb);

        for (var i = startIndex; i < loadedTimeline.Entries.Length; i++)
        {
            var entry = loadedTimeline.Entries[i];

            if (entry.Timestamp > endTime)
                break;

            if (entry.IsHidden)
                continue;

            // RW/TB already added from merged cache.
            if (entry.EntryType is TimelineEntryType.Raidwide or TimelineEntryType.TankBuster)
                continue;

            // Only include combat-relevant mechanics
            if (entry.EntryType is TimelineEntryType.Stack or TimelineEntryType.Spread
                or TimelineEntryType.Adds or TimelineEntryType.Enrage
                or TimelineEntryType.Ability)
            {
                var secondsUntil = entry.Timestamp - currentTime;
                results.Add(new MechanicPrediction(
                    secondsUntil,
                    entry.EntryType,
                    entry.Name,
                    confidence,
                    entry.Duration));
            }
        }

        results.Sort((a, b) => a.SecondsUntil.CompareTo(b.SecondsUntil));
        return results;
    }

    private FightTimeline CreateTestTimeline()
    {
        // Create a realistic test timeline that demonstrates the system
        var entries = new TimelineEntry[]
        {
            // Phase 1: Opening
            new(0.0f, "Combat Start", TimelineEntryType.Phase, null, 0f, -1f, "Phase1", false),
            new(5.0f, "Auto-Attack", TimelineEntryType.Ability, null, 0f, -1f, null, false),
            new(10.0f, "Raidwide Alpha", TimelineEntryType.Raidwide, null, 3.0f, -1f, null, false),
            new(18.0f, "Tank Buster", TimelineEntryType.TankBuster, null, 0f, -1f, null, false),
            new(25.0f, "Auto-Attack", TimelineEntryType.Ability, null, 0f, -1f, null, false),
            new(30.0f, "Stack Marker", TimelineEntryType.Stack, null, 5.0f, -1f, null, false),

            // Phase 2: Adds
            new(40.0f, "Phase 2", TimelineEntryType.Phase, null, 0f, -1f, "Phase2", false),
            new(42.0f, "Adds Spawn", TimelineEntryType.Adds, null, 20.0f, -1f, null, false),
            new(50.0f, "Raidwide Beta", TimelineEntryType.Raidwide, null, 3.0f, -1f, null, false),
            new(60.0f, "Spread Markers", TimelineEntryType.Spread, null, 4.0f, -1f, null, false),

            // Phase 3: Burn
            new(70.0f, "Phase 3", TimelineEntryType.Phase, null, 0f, -1f, "Phase3", false),
            new(75.0f, "Double Tank Buster", TimelineEntryType.TankBuster, null, 0f, -1f, null, false),
            new(85.0f, "Raidwide Gamma", TimelineEntryType.Raidwide, null, 3.0f, -1f, null, false),
            new(95.0f, "Stack Marker", TimelineEntryType.Stack, null, 5.0f, -1f, null, false),
            new(105.0f, "Raidwide Delta", TimelineEntryType.Raidwide, null, 3.0f, -1f, null, false),

            // Enrage
            new(120.0f, "Enrage", TimelineEntryType.Enrage, null, 0f, -1f, "Enrage", false),
        };

        return new FightTimeline(
            zoneId: 0,
            contentId: "test",
            name: "Test Fight (Simulation)",
            entries: entries);
    }

    #endregion

    #region Private Methods

    private void RefreshPredictionCache()
    {
        var cactbotRw = GetNextMechanicInternal(TimelineEntryType.Raidwide);
        var cactbotTb = GetNextMechanicInternal(TimelineEntryType.TankBuster);

        if (IsBossModTimelineEnabled() && bossModTimeline is { } ipc && ipc.Available && ipc.HasActiveModule())
        {
            cachedNextRaidwide = BossModTimelineMerge.MergeRaidwide(ipc.NextRaidwideIn(), cactbotRw);
            cachedNextTankBuster = BossModTimelineMerge.MergeTankBuster(ipc.NextTankbusterIn(), cactbotTb);
            return;
        }

        cachedNextRaidwide = cactbotRw;
        cachedNextTankBuster = cactbotTb;
    }

    private void UpdateBossModOnly()
    {
        if (!combatEventService.IsInCombat || !IsBossModTimelineLive())
        {
            if (cachedNextRaidwide != null || cachedNextTankBuster != null)
                ClearPredictionCache();
            return;
        }

        var now = DateTime.UtcNow;
        if ((now - lastBossModOnlyRefreshUtc).TotalSeconds < PredictionCacheRefreshInterval)
            return;

        lastBossModOnlyRefreshUtc = now;
        RefreshPredictionCache();
    }

    private bool IsBossModTimelineEnabled() =>
        configuration.Timeline.EnableTimelinePredictions
        && configuration.Timeline.EnableBossModTimelineIntegration;

    private bool IsBossModTimelineLive() =>
        IsBossModTimelineEnabled()
        && bossModTimeline is { } ipc
        && ipc.Available
        && ipc.HasActiveModule();

    private MechanicPrediction? GetNextMechanicInternal(TimelineEntryType type)
    {
        if (state == null || loadedTimeline == null)
            return null;

        var currentTime = state.CurrentTime;
        var confidence = state.Confidence;

        var startIndex = loadedTimeline.FindFirstEntryAtOrAfter(currentTime);

        for (var i = startIndex; i < loadedTimeline.Entries.Length; i++)
        {
            var entry = loadedTimeline.Entries[i];

            if (entry.EntryType == type && !entry.IsHidden)
            {
                var secondsUntil = entry.Timestamp - currentTime;
                return new MechanicPrediction(
                    secondsUntil,
                    entry.EntryType,
                    entry.Name,
                    confidence,
                    entry.Duration);
            }
        }

        return null;
    }

    private void ClearPredictionCache()
    {
        cachedNextRaidwide = null;
        cachedNextTankBuster = null;
        lastPredictionUpdateTime = 0f;
        lastBossModOnlyRefreshUtc = DateTime.MinValue;
    }

    private static string? LoadEmbeddedResource(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName);

        if (stream == null)
            return null;

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    #endregion

    public void Dispose()
    {
        Clear();
        log.Info("TimelineService: Disposed");
    }
}
