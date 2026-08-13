using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Common.Component.BGCollision;
using Olympus.Data;
using Olympus.Rotation.ApolloCore.Helpers;

namespace Olympus.Services.Targeting;

/// <summary>
/// Centralized targeting service with optimized filtering, caching, and multiple strategies.
/// </summary>
public sealed class TargetingService : ITargetingService
{
    private readonly IObjectTable _objectTable;
    private readonly IPartyList _partyList;
    private readonly ITargetManager _targetManager;
    private readonly Configuration _configuration;

    // Cache for valid enemies
    private readonly List<IBattleNpc> _cachedEnemies = new(32);
    private readonly Stopwatch _cacheTimer = new();
    private float _lastCacheRange;

    // Reusable work list for AoE target methods (all called from game thread)
    private readonly List<IBattleNpc> _aoeWorkList = new();

    // Scratch for pack-cluster / sole-hostile bootstrap scan (must not share _aoeWorkList)
    private readonly List<IBattleNpc> _bootstrapScratch = new(32);

    // Optional marker probe — null when not available (existing tests, no prod registration)
    private readonly IMarkerProbe? _markerProbe;

    // Reusable set for stop-mark IDs; rebuilt each GetValidEnemies cache flush (no per-frame alloc)
    private readonly HashSet<ulong> _stopMarkedIds = new(2);

    // Tracks when the hard target first became null so PauseWhenNoTarget can grace
    // brief Tab-retarget gaps without stalling DPS (gaze still pauses after grace).
    private long? _noTargetSinceTickMs;

    // Memo for OOC sole-hostile pull bootstrap / pack-cluster unlock (one scan per tick).
    private long _bootstrapScanTickMs = long.MinValue;
    private ulong _soleBootstrapHostileId;
    private readonly HashSet<ulong> _engagedPackClusterIds = new(16);

    // Tank job IDs: PLD=19, WAR=21, DRK=32, GNB=37
    private static readonly HashSet<uint> TankJobIds = [19, 21, 32, 37];

    public IGapCloserSafetyService GapCloserSafety { get; }

    public TargetingService(
        IObjectTable objectTable,
        IPartyList partyList,
        ITargetManager targetManager,
        Configuration configuration,
        IGapCloserSafetyService gapCloserSafety,
        IMarkerProbe? markerProbe = null)
    {
        _objectTable = objectTable;
        _partyList = partyList;
        _targetManager = targetManager;
        _configuration = configuration;
        GapCloserSafety = gapCloserSafety;
        _markerProbe = markerProbe;
        _cacheTimer.Start();
    }

    /// <summary>
    /// Always false — <c>PauseWhenNoTarget</c> no longer stalls damage targeting.
    /// Dual-boss swaps / Tab retargets used to freeze Find/Count after a short grace.
    /// </summary>
    public bool IsDamageTargetingPaused(IPlayerCharacter? player = null)
    {
        var hasHardTarget = _targetManager.Target != null;
        var noTargetDurationMs = UpdateNoTargetDurationMs(hasHardTarget);
        bool? playerInCombat = player == null
            ? null
            : (player.StatusFlags & StatusFlags.InCombat) != 0;

        return DamagePauseDecision.ShouldPause(
            pauseWhenNoTarget: _configuration.Targeting.PauseWhenNoTarget,
            hasHardTarget: hasHardTarget,
            playerInCombat: playerInCombat,
            noTargetDurationMs: noTargetDurationMs);
    }

    /// <summary>
    /// Advances / resets the null-hard-target timer. Returns continuous null duration in ms.
    /// </summary>
    private long UpdateNoTargetDurationMs(bool hasHardTarget)
    {
        if (hasHardTarget)
        {
            _noTargetSinceTickMs = null;
            return 0;
        }

        var now = Environment.TickCount64;
        _noTargetSinceTickMs ??= now;
        return now - _noTargetSinceTickMs.Value;
    }

    /// <summary>
    /// Whether CurrentTarget/FocusTarget may fall back to LowestHp this frame.
    /// Untargetable hard targets (diving shark) always allow fallback.
    /// </summary>
    private bool AllowExplicitTargetFallback()
    {
        var hard = _targetManager.Target;
        var hasHardTarget = hard != null;
        var hardTargetUsable = hard is IBattleNpc b && !b.IsDead && b.IsTargetable;
        var noTargetDurationMs = UpdateNoTargetDurationMs(hasHardTarget);
        return DamagePauseDecision.AllowExplicitTargetFallback(
            strictCurrentTargetStrategy: _configuration.Targeting.StrictCurrentTargetStrategy,
            hasHardTarget: hasHardTarget,
            noTargetDurationMs: noTargetDurationMs,
            hardTargetUsable: hardTargetUsable);
    }

    /// <inheritdoc />
    public IBattleNpc? GetUserEnemyTarget()
    {
        return _targetManager.Target is IBattleNpc enemy && IsStillValid(enemy) ? enemy : null;
    }

    /// <summary>
    /// Finds an enemy target using the specified strategy.
    /// </summary>
    /// <param name="strategy">Targeting strategy to use.</param>
    /// <param name="maxRange">Maximum range in yalms.</param>
    /// <param name="player">Current player character.</param>
    /// <returns>Best target according to strategy, or null if none found.</returns>
    public IBattleNpc? FindEnemy(EnemyTargetingStrategy strategy, float maxRange, IPlayerCharacter player)
    {
        // Hard pause: sustained null hard target with PauseWhenNoTarget (gaze / disengage).
        // Brief Tab-retarget gaps and OOC bootstrap are not paused — see DamagePauseDecision.
        if (IsDamageTargetingPaused(player))
            return null;

        // Try primary strategy
        var target = FindEnemyByStrategy(strategy, maxRange, player);

        // If TankAssist fails and fallback is enabled, try LowestHp
        if (target == null && strategy == EnemyTargetingStrategy.TankAssist && _configuration.Targeting.UseTankAssistFallback)
        {
            target = FindEnemyByStrategy(EnemyTargetingStrategy.LowestHp, maxRange, player);
        }

        // If CurrentTarget/FocusTarget fails, fall back to LowestHp — unless strict mode
        // is committed (sustained null past grace). During the retarget grace window even
        // strict mode falls back so Tab between pack members does not stall DPS.
        if (target == null && strategy is EnemyTargetingStrategy.CurrentTarget or EnemyTargetingStrategy.FocusTarget
            && AllowExplicitTargetFallback())
        {
            target = FindEnemyByStrategy(EnemyTargetingStrategy.LowestHp, maxRange, player);
        }

        return target;
    }

    /// <summary>
    /// Finds an enemy that needs DoT applied or refreshed. Respects the configured
    /// <see cref="EnemyTargetingStrategy"/> — on explicit strategies (CurrentTarget,
    /// FocusTarget) the DoT will never spill onto enemies the player did not pick.
    /// On aggregate strategies (LowestHp, HighestHp, Nearest, TankAssist) the DoT
    /// still goes to the strategy's chosen enemy, but only if that enemy actually
    /// needs the DoT applied or refreshed.
    /// </summary>
    /// <param name="dotStatusId">Status ID to check for (Aero/Dia variant).</param>
    /// <param name="refreshThreshold">Seconds remaining before DoT should be refreshed.</param>
    /// <param name="maxRange">Maximum range in yalms.</param>
    /// <param name="player">Current player character.</param>
    /// <returns>Enemy needing DoT under the current strategy, or null.</returns>
    public IBattleNpc? FindEnemyNeedingDot(
        uint dotStatusId,
        float refreshThreshold,
        float maxRange,
        IPlayerCharacter player)
    {
        // Hard pause: sustained null hard target — don't DoT anything.
        if (IsDamageTargetingPaused(player))
            return null;

        var strategy = _configuration.Targeting.EnemyStrategy;

        // Explicit-target strategies: only consider the player's selected target/focus,
        // never spread DoT to unrelated enemies. This is the "smart DoT" safety fix —
        // hitting an add that isn't supposed to take damage (reflect, vulnerability down,
        // damage debuff) breaks fights, so honor player intent here.
        if (strategy is EnemyTargetingStrategy.CurrentTarget or EnemyTargetingStrategy.FocusTarget)
        {
            var explicitTarget = strategy == EnemyTargetingStrategy.CurrentTarget
                ? _targetManager.Target as IBattleNpc
                : _targetManager.FocusTarget as IBattleNpc;

            if (explicitTarget == null || !IsStillValid(explicitTarget))
                return null;

            if (!DistanceHelper.IsInRange(player.Position, explicitTarget.Position, maxRange + explicitTarget.HitboxRadius + player.HitboxRadius))
                return null;

            return GetDotDuration(explicitTarget, dotStatusId) < refreshThreshold ? explicitTarget : null;
        }

        // Aggregate strategies: pick the strategy's best enemy, but only DoT it if it
        // actually needs the DoT. This prevents the old behavior of scanning every
        // in-combat enemy and targeting whichever had the lowest DoT duration.
        IBattleNpc? strategyTarget = strategy switch
        {
            EnemyTargetingStrategy.TankAssist => FindEnemyByStrategy(strategy, maxRange, player),
            EnemyTargetingStrategy.Nearest => FindEnemyByStrategy(strategy, maxRange, player),
            EnemyTargetingStrategy.HighestHp => FindEnemyByStrategy(strategy, maxRange, player),
            _ => FindEnemyByStrategy(EnemyTargetingStrategy.LowestHp, maxRange, player)
        };

        if (strategyTarget == null)
            return null;

        return GetDotDuration(strategyTarget, dotStatusId) < refreshThreshold ? strategyTarget : null;
    }

    /// <summary>
    /// Counts the number of valid enemies within the specified radius of the player.
    /// Used for AoE damage decisions (e.g., Holy when 3+ enemies).
    /// </summary>
    /// <param name="radius">Radius in yalms to check.</param>
    /// <param name="player">Current player character.</param>
    /// <returns>Number of valid enemies within radius.</returns>
    public int CountEnemiesInRange(float radius, IPlayerCharacter player)
    {
        if (IsDamageTargetingPaused(player))
            return 0;

        // AoE threshold counting ignores LoS — pack members behind each other / pillars
        // must still raise the count so we switch off ST in full pulls.
        int count = 0;
        CollectHostilesInRange(radius, player, requireLineOfSight: false, _aoeWorkList);
        for (var i = 0; i < _aoeWorkList.Count; i++)
        {
            if (!IsEnemySelectableForDamage(_aoeWorkList[i], player))
                continue;
            count++;
        }
        return count;
    }

    /// <summary>
    /// Finds the enemy that has the most other enemies within the specified radius.
    /// Used for targeted AoE spells like Glare IV and Afflatus Misery.
    /// </summary>
    public (IBattleNpc? target, int hitCount) FindBestAoETarget(float aoeRadius, float maxRange, IPlayerCharacter player)
    {
        if (IsDamageTargetingPaused(player))
            return (null, 0);

        IBattleNpc? bestTarget = null;
        int bestHitCount = 0;

        // Ignore LoS for AoE decisions (same as CountEnemiesInRange).
        CollectHostilesInRange(maxRange, player, requireLineOfSight: false, _aoeWorkList);
        for (var i = _aoeWorkList.Count - 1; i >= 0; i--)
        {
            if (!IsEnemySelectableForDamage(_aoeWorkList[i], player))
                _aoeWorkList.RemoveAt(i);
        }

        if (_aoeWorkList.Count == 0)
            return (null, 0);

        if (_aoeWorkList.Count == 1)
            return (_aoeWorkList[0], 1);

        foreach (var potentialTarget in _aoeWorkList)
        {
            int hitCount = 1;

            foreach (var other in _aoeWorkList)
            {
                if (other.EntityId == potentialTarget.EntityId)
                    continue;

                var hitRadius = aoeRadius + other.HitboxRadius;
                var distSquared = Vector3.DistanceSquared(potentialTarget.Position, other.Position);
                if (distSquared <= hitRadius * hitRadius)
                    hitCount++;
            }

            if (hitCount > bestHitCount)
            {
                bestHitCount = hitCount;
                bestTarget = potentialTarget;
            }
        }

        return (bestTarget, bestHitCount);
    }

    /// <inheritdoc />
    public IBattleNpc? FindEnemyForAction(EnemyTargetingStrategy strategy, uint actionId, IPlayerCharacter player)
    {
        if (IsDamageTargetingPaused(player))
            return null;

        var target = FindEnemyByActionStrategy(strategy, actionId, player);

        if (target == null && strategy == EnemyTargetingStrategy.TankAssist && _configuration.Targeting.UseTankAssistFallback)
            target = FindEnemyByActionStrategy(EnemyTargetingStrategy.LowestHp, actionId, player);

        // Fall back from explicit-target strategies to LowestHp during retarget grace or
        // when strict mode is off. Sustained null + strict stays empty (gaze / stop).
        if (target == null && strategy is EnemyTargetingStrategy.CurrentTarget or EnemyTargetingStrategy.FocusTarget
            && AllowExplicitTargetFallback())
            target = FindEnemyByActionStrategy(EnemyTargetingStrategy.LowestHp, actionId, player);

        return target;
    }

    /// <summary>
    /// Invalidates the enemy cache. Call when targets may have changed significantly.
    /// </summary>
    public void InvalidateCache()
    {
        _cachedEnemies.Clear();
        _cacheTimer.Restart();
    }

    private IBattleNpc? FindEnemyByActionStrategy(EnemyTargetingStrategy strategy, uint actionId, IPlayerCharacter player)
    {
        // NOTE: Attack marker promotion is NOT applied here, only in FindEnemyByStrategy.
        // Add the UseAttackMarkers promotion check if this method gains callers that expect marker-aware results.
        return strategy switch
        {
            EnemyTargetingStrategy.LowestHp => FindLowestHpEnemyForAction(actionId, player),
            EnemyTargetingStrategy.HighestHp => FindHighestHpEnemyForAction(actionId, player),
            EnemyTargetingStrategy.Nearest => FindNearestEnemyForAction(actionId, player),
            EnemyTargetingStrategy.TankAssist => FindTankTargetForAction(actionId, player),
            EnemyTargetingStrategy.CurrentTarget => FindCurrentTargetForAction(actionId, player),
            EnemyTargetingStrategy.FocusTarget => FindFocusTargetForAction(actionId, player),
            _ => FindLowestHpEnemyForAction(actionId, player)
        };
    }

    private IBattleNpc? FindLowestHpEnemyForAction(uint actionId, IPlayerCharacter player)
    {
        IBattleNpc? best = null;
        uint lowestHp = uint.MaxValue;
        foreach (var candidate in GetActionCandidates(player))
        {
            if (!IsActionInRange(actionId, player, candidate)) continue;
            if (candidate.CurrentHp < lowestHp)
            {
                lowestHp = candidate.CurrentHp;
                best = candidate;
            }
        }
        return best;
    }

    private IBattleNpc? FindHighestHpEnemyForAction(uint actionId, IPlayerCharacter player)
    {
        IBattleNpc? best = null;
        uint highestHp = 0;
        foreach (var candidate in GetActionCandidates(player))
        {
            if (!IsActionInRange(actionId, player, candidate)) continue;
            if (candidate.CurrentHp > highestHp)
            {
                highestHp = candidate.CurrentHp;
                best = candidate;
            }
        }
        return best;
    }

    private IBattleNpc? FindNearestEnemyForAction(uint actionId, IPlayerCharacter player)
    {
        IBattleNpc? best = null;
        float nearestDist = float.MaxValue;
        var playerPos = player.Position;
        foreach (var candidate in GetActionCandidates(player))
        {
            if (!IsActionInRange(actionId, player, candidate)) continue;
            var dist = Vector3.DistanceSquared(playerPos, candidate.Position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                best = candidate;
            }
        }
        return best;
    }

    private IBattleNpc? FindTankTargetForAction(uint actionId, IPlayerCharacter player)
    {
        // Same MT-by-proxy heuristic as FindTankTarget: prefer the tank whose target
        // has the highest MaxHp. Pending a real enmity API.
        var candidateEnemies = new List<IBattleNpc>(2);

        foreach (var member in _partyList)
        {
            if (member.GameObject is not IBattleChara chara) continue;
            if (!TankJobIds.Contains(chara.ClassJob.RowId)) continue;
            var targetId = chara.TargetObjectId;
            if (targetId is 0 or 0xE0000000) continue;
            var target = _objectTable.SearchById(targetId);
            if (target is IBattleNpc enemy && IsStillValid(enemy) && IsActionInRange(actionId, player, enemy))
                candidateEnemies.Add(enemy);
        }

        if (candidateEnemies.Count == 0)
            return null;
        if (candidateEnemies.Count == 1)
            return candidateEnemies[0];

        var maxHps = new uint?[candidateEnemies.Count];
        for (int i = 0; i < candidateEnemies.Count; i++)
            maxHps[i] = candidateEnemies[i].MaxHp;
        var idx = SelectMainTankCandidateIndex(maxHps);
        return idx >= 0 ? candidateEnemies[idx] : candidateEnemies[0];
    }

    private IBattleNpc? FindCurrentTargetForAction(uint actionId, IPlayerCharacter player)
    {
        var target = _targetManager.Target;
        if (target is IBattleNpc enemy && IsStillValid(enemy) && IsActionInRange(actionId, player, enemy))
            return enemy;
        return null;
    }

    private IBattleNpc? FindFocusTargetForAction(uint actionId, IPlayerCharacter player)
    {
        var target = _targetManager.FocusTarget;
        if (target is IBattleNpc enemy && IsStillValid(enemy) && IsActionInRange(actionId, player, enemy))
            return enemy;
        return null;
    }

    /// <summary>
    /// Iterates all nearby battle NPCs as candidates for action-based range checks.
    /// Uses a generous 15y pre-filter (safe for any 3y melee action including large boss hitboxes).
    /// </summary>
    private IEnumerable<IBattleNpc> GetActionCandidates(IPlayerCharacter player)
    {
        foreach (var obj in _objectTable)
        {
            if (obj.ObjectKind != ObjectKind.BattleNpc) continue;
            if (!obj.IsTargetable) continue;
            if (obj.IsDead) continue;
            if (obj.CurrentDistance > 15) continue;
            if (obj is not IBattleNpc npc) continue;
            if ((byte)npc.BattleNpcKind != Olympus.Compat.BattleNpcKinds.Combatant && npc.SubKind != 0) continue;
            if (_configuration.Targeting.EnableInvulnerabilityFiltering &&
                HasInvulnerabilityStatus(npc))
                continue;
            yield return npc;
        }
    }

    /// <summary>
    /// Uses the game's native GetActionInRangeOrLoS to check if an action can reach a target.
    /// Returns true for result 0 (in range + LoS) or 565 (in range but facing wrong way).
    /// </summary>
    private static unsafe bool IsActionInRange(uint actionId, IGameObject player, IGameObject target)
    {
        try
        {
            var playerStruct = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)player.Address;
            var targetStruct = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)target.Address;
            if (playerStruct == null || targetStruct == null) return false;
            var result = ActionManager.GetActionInRangeOrLoS(actionId, playerStruct, targetStruct);
            return result is 0 or 565; // 0=in range+LoS, 565=in range but facing wrong way
        }
        catch
        {
            return false;
        }
    }

    private IBattleNpc? FindEnemyByStrategy(EnemyTargetingStrategy strategy, float maxRange, IPlayerCharacter player)
    {
        // Attack-marker promotion: for aggregate strategies only (never CurrentTarget/FocusTarget —
        // those respect the player's explicit selection). When attack marks are set, the marked
        // enemy in the highest-priority slot wins ahead of the configured HP/distance strategy.
        if (_markerProbe != null && _configuration.Targeting.UseAttackMarkers &&
            strategy is not EnemyTargetingStrategy.CurrentTarget and not EnemyTargetingStrategy.FocusTarget)
        {
            var attackTarget = FindFirstAttackMarkedEnemy(maxRange, player);
            if (attackTarget != null) return attackTarget;
        }

        return strategy switch
        {
            EnemyTargetingStrategy.LowestHp => FindLowestHpEnemy(maxRange, player),
            EnemyTargetingStrategy.HighestHp => FindHighestHpEnemy(maxRange, player),
            EnemyTargetingStrategy.Nearest => FindNearestEnemy(maxRange, player),
            EnemyTargetingStrategy.TankAssist => FindTankTarget(maxRange, player),
            EnemyTargetingStrategy.CurrentTarget => FindCurrentTarget(maxRange, player),
            EnemyTargetingStrategy.FocusTarget => FindFocusTarget(maxRange, player),
            _ => FindLowestHpEnemy(maxRange, player)
        };
    }

    /// <summary>
    /// Iterates attack marker slots in Attack1..Attack8 priority order and returns the
    /// first marked enemy that is present in the current valid-enemy set and in combat.
    /// Returns null if no attack marks are set or none resolve to a valid in-range enemy.
    /// Uses <see cref="_aoeWorkList"/> as a temporary scratch buffer (no per-call allocation).
    /// </summary>
    private IBattleNpc? FindFirstAttackMarkedEnemy(float maxRange, IPlayerCharacter player)
    {
        var attackIds = _markerProbe!.GetAttackMarkTargets();

        // Collect selectable enemies into the reusable work list (cache hit = fast)
        _aoeWorkList.Clear();
        foreach (var e in GetValidEnemies(maxRange, player))
        {
            if (IsEnemySelectableForDamage(e, player))
                _aoeWorkList.Add(e);
        }

        if (_aoeWorkList.Count == 0) return null;

        // Return the first marker slot (highest priority) whose ID matches a valid enemy
        foreach (var markId in attackIds)
        {
            if (markId == 0) continue;
            foreach (var e in _aoeWorkList)
                if (e.GameObjectId == markId) return e;
        }
        return null;
    }

    /// <summary>
    /// Shared engagement gate for damage targeting (Count / Find* / AoE best-target).
    /// Always allows the hard target (dummies, intentional pulls). While the player is
    /// in combat, every valid hostile is selectable — pack adds often lag on
    /// <see cref="StatusFlags.InCombat"/> after a pull. Out of combat, enemies already
    /// flagged InCombat are selectable (tank-pulled boss). Additionally, a sole
    /// targetable hostile within bootstrap range is selectable so boss-seal pulls can
    /// start before either InCombat flag flips — multi-mob trash stays blocked.
    /// </summary>
    private bool IsEnemySelectableForDamage(IBattleNpc enemy, IPlayerCharacter player)
    {
        var isHardTarget = _targetManager.Target is IBattleNpc hardTarget
            && hardTarget.GameObjectId == enemy.GameObjectId;
        var playerInCombat = (player.StatusFlags & StatusFlags.InCombat) != 0;
        var enemyInCombat = (enemy.StatusFlags & StatusFlags.InCombat) != 0;
        EnsureBootstrapScan(player);
        var isSole = _soleBootstrapHostileId != 0UL && _soleBootstrapHostileId == enemy.GameObjectId;
        var inCluster = _engagedPackClusterIds.Contains(enemy.GameObjectId);

        return DamageEngagementDecision.IsSelectable(
            isHardTarget,
            playerInCombat,
            enemyInCombat,
            isSole,
            inCluster);
    }

    /// <summary>
    /// One object-table scan per tick: sole-hostile bootstrap id + engaged pack cluster
    /// (InCombat seeds + hard target, plus hostiles within PackClusterLinkYalms).
    /// Does not touch the GetValidEnemies cache.
    /// </summary>
    private void EnsureBootstrapScan(IPlayerCharacter player)
    {
        var now = Environment.TickCount64;
        if (_bootstrapScanTickMs == now)
            return;

        _bootstrapScanTickMs = now;
        _soleBootstrapHostileId = 0UL;
        _engagedPackClusterIds.Clear();

        var playerPos = player.Position;
        var maxRange = DamageEngagementDecision.PullBootstrapRangeYalms;
        var maxRangeYalms = (byte)Math.Ceiling(maxRange);
        var linkSq = DamageEngagementDecision.PackClusterLinkYalms * DamageEngagementDecision.PackClusterLinkYalms;
        var hardTargetId = _targetManager.Target is IBattleNpc ht ? ht.GameObjectId : 0UL;

        // Pass 1: collect hostiles in bootstrap range; track sole id + InCombat/hard seeds.
        _bootstrapScratch.Clear();
        ulong soleId = 0UL;
        var hostileCount = 0;

        foreach (var obj in _objectTable)
        {
            if (obj.ObjectKind != ObjectKind.BattleNpc)
                continue;
            if (obj is not IBattleNpc npc)
                continue;
            if (!obj.IsTargetable || obj.IsDead)
                continue;
            if ((byte)npc.BattleNpcKind != Olympus.Compat.BattleNpcKinds.Combatant && npc.SubKind != 0)
                continue;
            if (obj.CurrentDistance > maxRangeYalms + (int)Math.Ceiling(obj.HitboxRadius))
                continue;

            var effectiveRange = maxRange + npc.HitboxRadius + player.HitboxRadius;
            if (Vector3.DistanceSquared(playerPos, npc.Position) > effectiveRange * effectiveRange)
                continue;

            hostileCount++;
            soleId = hostileCount == 1 ? npc.GameObjectId : 0UL;
            _bootstrapScratch.Add(npc);

            var isHard = hardTargetId != 0UL && npc.GameObjectId == hardTargetId;
            if (isHard || (npc.StatusFlags & StatusFlags.InCombat) != 0)
                _engagedPackClusterIds.Add(npc.GameObjectId);
        }

        _soleBootstrapHostileId = hostileCount == 1 ? soleId : 0UL;

        // Pass 2: flood-fill pack cluster so chained adds (A–B–C) all unlock for AoE.
        if (_engagedPackClusterIds.Count == 0 || _bootstrapScratch.Count == 0)
            return;

        bool grew;
        do
        {
            grew = false;
            for (var i = 0; i < _bootstrapScratch.Count; i++)
            {
                var candidate = _bootstrapScratch[i];
                if (_engagedPackClusterIds.Contains(candidate.GameObjectId))
                    continue;

                for (var j = 0; j < _bootstrapScratch.Count; j++)
                {
                    var seed = _bootstrapScratch[j];
                    if (!_engagedPackClusterIds.Contains(seed.GameObjectId))
                        continue;

                    if (Vector3.DistanceSquared(candidate.Position, seed.Position) <= linkSq)
                    {
                        _engagedPackClusterIds.Add(candidate.GameObjectId);
                        grew = true;
                        break;
                    }
                }
            }
        } while (grew);
    }

    private ulong GetSoleBootstrapHostileId(IPlayerCharacter player)
    {
        EnsureBootstrapScan(player);
        return _soleBootstrapHostileId;
    }

    private IBattleNpc? FindLowestHpEnemy(float maxRange, IPlayerCharacter player)
    {
        IBattleNpc? best = null;
        uint lowestHp = uint.MaxValue;

        foreach (var enemy in GetValidEnemies(maxRange, player))
        {
            if (!IsEnemySelectableForDamage(enemy, player))
                continue;

            if (enemy.CurrentHp < lowestHp)
            {
                lowestHp = enemy.CurrentHp;
                best = enemy;
            }
        }

        return best;
    }

    private IBattleNpc? FindHighestHpEnemy(float maxRange, IPlayerCharacter player)
    {
        IBattleNpc? best = null;
        uint highestHp = 0;

        foreach (var enemy in GetValidEnemies(maxRange, player))
        {
            if (!IsEnemySelectableForDamage(enemy, player))
                continue;

            if (enemy.CurrentHp > highestHp)
            {
                highestHp = enemy.CurrentHp;
                best = enemy;
            }
        }

        return best;
    }

    private IBattleNpc? FindNearestEnemy(float maxRange, IPlayerCharacter player)
    {
        IBattleNpc? best = null;
        float nearestDist = float.MaxValue;
        var playerPos = player.Position;

        foreach (var enemy in GetValidEnemies(maxRange, player))
        {
            if (!IsEnemySelectableForDamage(enemy, player))
                continue;

            var dist = Vector3.DistanceSquared(playerPos, enemy.Position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                best = enemy;
            }
        }

        return best;
    }

    /// <summary>
    /// Returns the index of the tank candidate whose current target has the highest MaxHp,
    /// or -1 if all entries are null (no tank has a valid target). Serves as a
    /// main-tank-by-proxy heuristic: the main tank almost always holds the boss, which
    /// has significantly higher MaxHp than any add. Pending a real enmity API.
    /// </summary>
    /// <param name="targetMaxHps">MaxHp of each tank's current valid target, or null if
    /// the tank has no valid target or that target is out of range.</param>
    internal static int SelectMainTankCandidateIndex(uint?[] targetMaxHps)
    {
        int bestIdx = -1;
        uint bestMaxHp = 0;
        for (int i = 0; i < targetMaxHps.Length; i++)
        {
            if (targetMaxHps[i] is { } hp && hp > bestMaxHp)
            {
                bestMaxHp = hp;
                bestIdx = i;
            }
        }
        return bestIdx;
    }

    private IBattleNpc? FindTankTarget(float maxRange, IPlayerCharacter player)
    {
        // Collect all valid tank targets in party-list order. When multiple tanks have
        // targets, SelectMainTankCandidateIndex picks the one whose target has the highest
        // MaxHp -- a main-tank-by-proxy heuristic (boss MaxHp >> add MaxHp in any raid).
        // Pending a real enmity API, this is the best available signal.
        var candidateEnemies = new List<IBattleNpc>(2); // at most 2 tanks in a standard party

        foreach (var member in _partyList)
        {
            if (member.GameObject is not IBattleChara chara)
                continue;
            if (!TankJobIds.Contains(chara.ClassJob.RowId))
                continue;
            var targetId = chara.TargetObjectId;
            if (targetId is 0 or 0xE0000000)
                continue;
            var target = _objectTable.SearchById(targetId);
            if (target is IBattleNpc enemy && IsValidEnemy(enemy, maxRange, player))
                candidateEnemies.Add(enemy);
        }

        if (candidateEnemies.Count == 0)
            return null;
        if (candidateEnemies.Count == 1)
            return candidateEnemies[0];

        var maxHps = new uint?[candidateEnemies.Count];
        for (int i = 0; i < candidateEnemies.Count; i++)
            maxHps[i] = candidateEnemies[i].MaxHp;
        var idx = SelectMainTankCandidateIndex(maxHps);
        return idx >= 0 ? candidateEnemies[idx] : candidateEnemies[0];
    }

    private IBattleNpc? FindCurrentTarget(float maxRange, IPlayerCharacter player)
    {
        var target = _targetManager.Target;
        if (target is not IBattleNpc enemy)
            return null;

        // Usable hard target.
        if (enemy.IsTargetable && IsValidEnemy(enemy, maxRange, player))
            return enemy;

        // Untargetable hard target (Anyder water dive, brief boss flicker): only keep it when
        // nothing else is selectable. Otherwise return null so LowestHp can pick the sibling.
        if (!enemy.IsTargetable && IsValidEnemy(enemy, maxRange, player, allowUntargetable: true))
        {
            foreach (var other in GetValidEnemies(maxRange, player))
            {
                if (other.GameObjectId != enemy.GameObjectId)
                    return null;
            }

            return enemy;
        }

        return null;
    }

    private IBattleNpc? FindFocusTarget(float maxRange, IPlayerCharacter player)
    {
        var target = _targetManager.FocusTarget;
        if (target is IBattleNpc enemy && IsValidEnemy(enemy, maxRange, player))
            return enemy;

        return null;
    }

    /// <summary>
    /// Gets valid enemies in range, using cache when available (LoS applied when configured).
    /// </summary>
    private IEnumerable<IBattleNpc> GetValidEnemies(float maxRange, IPlayerCharacter player)
    {
        // Check if cache is still valid
        var cacheAge = _cacheTimer.ElapsedMilliseconds;
        if (_cachedEnemies.Count > 0 &&
            cacheAge < _configuration.Targeting.TargetCacheTtlMs &&
            Math.Abs(_lastCacheRange - maxRange) < 0.1f)
        {
            _cachedEnemies.RemoveAll(e => !IsStillValid(e));

            if (_cachedEnemies.Count > 0)
            {
                foreach (var enemy in _cachedEnemies)
                    yield return enemy;
                yield break;
            }
        }

        CollectHostilesInRange(maxRange, player, requireLineOfSight: true, _cachedEnemies);
        _lastCacheRange = maxRange;
        _cacheTimer.Restart();

        foreach (var enemy in _cachedEnemies)
            yield return enemy;
    }

    /// <summary>
    /// Collects hostiles in range into <paramref name="into"/>. Used by ST Find (with LoS)
    /// and AoE Count/FindBestAoETarget (without LoS so full packs are not under-counted).
    /// </summary>
    private void CollectHostilesInRange(
        float maxRange,
        IPlayerCharacter player,
        bool requireLineOfSight,
        List<IBattleNpc> into)
    {
        into.Clear();

        _stopMarkedIds.Clear();
        if (_configuration.Targeting.FilterStopMarkers && _markerProbe != null)
        {
            foreach (var id in _markerProbe.GetStopMarkTargets())
                if (id != 0) _stopMarkedIds.Add(id);
        }

        var playerPos = player.Position;
        var maxRangeYalms = (byte)Math.Ceiling(maxRange);
        var hardTargetId = _targetManager.Target is IBattleNpc hardTarget
            ? hardTarget.GameObjectId
            : 0UL;
        var losEnabled = requireLineOfSight && _configuration.Targeting.EnableLineOfSightFiltering;

        foreach (var obj in _objectTable)
        {
            if (obj.ObjectKind != ObjectKind.BattleNpc)
                continue;
            if (obj is not IBattleNpc npc)
                continue;

            var isHardTarget = hardTargetId != 0UL && npc.GameObjectId == hardTargetId;

            if (!obj.IsTargetable && !isHardTarget)
                continue;
            if (obj.IsDead)
                continue;
            if (obj.CurrentDistance > maxRangeYalms + (int)Math.Ceiling(obj.HitboxRadius))
                continue;
            if ((byte)npc.BattleNpcKind != Olympus.Compat.BattleNpcKinds.Combatant && npc.SubKind != 0)
                continue;

            var effectiveRange = maxRange + npc.HitboxRadius + player.HitboxRadius;
            if (Vector3.DistanceSquared(playerPos, npc.Position) > effectiveRange * effectiveRange)
                continue;

            if (!isHardTarget && losEnabled && !HasLineOfSight(playerPos, npc.Position))
                continue;

            if (!isHardTarget &&
                _configuration.Targeting.EnableInvulnerabilityFiltering &&
                HasInvulnerabilityStatus(npc))
                continue;

            if (!isHardTarget &&
                _stopMarkedIds.Count > 0 &&
                _stopMarkedIds.Contains(npc.GameObjectId))
                continue;

            into.Add(npc);
        }

        var hasTargetable = false;
        for (var i = 0; i < into.Count; i++)
        {
            if (into[i].IsTargetable)
            {
                hasTargetable = true;
                break;
            }
        }

        if (hasTargetable)
            into.RemoveAll(static e => !e.IsTargetable);
    }

    private bool IsValidEnemy(IBattleNpc enemy, float maxRange, IPlayerCharacter player, bool allowUntargetable = false)
    {
        if (enemy.IsDead)
            return false;

        if (!allowUntargetable && !enemy.IsTargetable)
            return false;

        if ((byte)enemy.BattleNpcKind != Olympus.Compat.BattleNpcKinds.Combatant && enemy.SubKind != 0)
            return false;

        return DistanceHelper.IsInRange(player.Position, enemy.Position, maxRange + enemy.HitboxRadius + player.HitboxRadius);
    }

    private bool IsStillValid(IBattleNpc enemy)
    {
        if (enemy.IsDead)
            return false;

        // Drop untargetable entries from the cache when a targetable sibling exists so
        // dual-boss dives do not keep the underwater shark selected across TTL frames.
        if (enemy.IsTargetable)
            return true;

        if (_targetManager.Target is IBattleNpc hard
            && hard.GameObjectId == enemy.GameObjectId)
        {
            for (var i = 0; i < _cachedEnemies.Count; i++)
            {
                var other = _cachedEnemies[i];
                if (other.GameObjectId != enemy.GameObjectId && other.IsTargetable)
                    return false;
            }

            return true; // sole candidate — brief flicker
        }

        return false;
    }

    /// <summary>
    /// Checks line of sight from the player's approximate eye height to an enemy position
    /// using a BGCollision raycast. Returns false if geometry blocks the path.
    /// </summary>
    private static unsafe bool HasLineOfSight(Vector3 playerPos, Vector3 enemyPos)
    {
        try
        {
            var eyePos = playerPos with { Y = playerPos.Y + 2f };
            var direction = enemyPos - eyePos;
            var distance = direction.Length();
            if (distance < 0.01f)
                return true;

            direction /= distance;
            return !BGCollisionModule.RaycastMaterialFilter(eyePos, direction, out _, distance);
        }
        catch
        {
            // BGCollision unavailable (loading screen, etc.) — assume LoS is fine
            return true;
        }
    }

    private static float GetDotDuration(IBattleChara target, uint statusId)
    {
        if (target.StatusList == null)
            return 0f;
        foreach (var status in target.StatusList)
        {
            if (status.StatusId == statusId)
                return status.RemainingTime;
        }
        return 0f;
    }

    /// <summary>
    /// Checks whether an enemy has a known invulnerability status effect.
    /// Used to skip immune targets during auto-targeting (boss phase transitions,
    /// invulnerable adds, untouchable objects like ARR crystals).
    /// </summary>
    private static bool HasInvulnerabilityStatus(IBattleNpc npc)
    {
        if (npc.StatusList == null)
            return false;

        foreach (var status in npc.StatusList)
        {
            if (FFXIVConstants.EnemyInvulnerabilityStatusIds.Contains(status.StatusId))
                return true;
        }

        return false;
    }

    // ── Cone / Line AoE Targeting ──

    /// <inheritdoc />
    public (IBattleNpc? target, int hitCount, float optimalAngle) FindBestConeAoETarget(
        float coneHalfAngle, float radius, float maxRange, IPlayerCharacter player)
    {
        if (IsDamageTargetingPaused(player))
            return (null, 0, 0f);

        // Use the ability's effect range for candidate filtering, not the rotation's targeting range
        var candidateRange = MathF.Max(radius, maxRange);
        _aoeWorkList.Clear();
        foreach (var e in GetValidEnemies(candidateRange, player))
        {
            if (!IsEnemySelectableForDamage(e, player))
                continue;
            _aoeWorkList.Add(e);
        }

        if (_aoeWorkList.Count == 0) return (null, 0, 0f);
        if (_aoeWorkList.Count == 1)
        {
            var dx = _aoeWorkList[0].Position.X - player.Position.X;
            var dz = _aoeWorkList[0].Position.Z - player.Position.Z;
            return (_aoeWorkList[0], 1, MathF.Atan2(dx, dz));
        }

        int bestCount = 0;
        float bestAngle = 0f;
        IBattleNpc? bestTarget = null;
        var playerPos = player.Position;

        // For each enemy as potential target: aim at them and count how many others
        // the cone/line would clip. We can only face enemies we target (game auto-faces).
        foreach (var candidate in _aoeWorkList)
        {
            var dx = candidate.Position.X - playerPos.X;
            var dz = candidate.Position.Z - playerPos.Z;
            var aimAngle = MathF.Atan2(dx, dz);

            var count = 0;
            foreach (var e in _aoeWorkList)
            {
                var edx = e.Position.X - playerPos.X;
                var edz = e.Position.Z - playerPos.Z;
                var dist = MathF.Sqrt(edx * edx + edz * edz);
                if (dist - e.HitboxRadius > radius) continue;

                var angleToE = MathF.Atan2(edx, edz);
                var diff = NormalizeAngle(angleToE - aimAngle);
                if (MathF.Abs(diff) <= coneHalfAngle)
                    count++;
            }

            if (count > bestCount)
            {
                bestCount = count;
                bestAngle = aimAngle;
                bestTarget = candidate;
            }
        }

        return (bestTarget, bestCount, bestAngle);
    }

    /// <inheritdoc />
    public (IBattleNpc? target, int hitCount, float optimalAngle) FindBestLineAoETarget(
        float lineWidth, float length, float maxRange, IPlayerCharacter player)
    {
        if (IsDamageTargetingPaused(player))
            return (null, 0, 0f);

        // Use the ability's effect range for candidate filtering, not the rotation's targeting range
        var candidateRange = MathF.Max(length, maxRange);
        _aoeWorkList.Clear();
        foreach (var e in GetValidEnemies(candidateRange, player))
        {
            if (!IsEnemySelectableForDamage(e, player))
                continue;
            _aoeWorkList.Add(e);
        }

        if (_aoeWorkList.Count == 0) return (null, 0, 0f);
        if (_aoeWorkList.Count == 1)
        {
            var dx = _aoeWorkList[0].Position.X - player.Position.X;
            var dz = _aoeWorkList[0].Position.Z - player.Position.Z;
            return (_aoeWorkList[0], 1, MathF.Atan2(dx, dz));
        }

        int bestCount = 0;
        float bestAngle = 0f;
        IBattleNpc? bestTarget = null;
        var playerPos = player.Position;
        var halfWidth = lineWidth * 0.5f;

        // For each enemy as potential target: aim at them and count how many others
        // the line would clip. We can only face enemies we target.
        foreach (var candidate in _aoeWorkList)
        {
            var dx = candidate.Position.X - playerPos.X;
            var dz = candidate.Position.Z - playerPos.Z;
            var aimAngle = MathF.Atan2(dx, dz);
            var sinH = MathF.Sin(aimAngle);
            var cosH = MathF.Cos(aimAngle);

            var count = 0;
            foreach (var e in _aoeWorkList)
            {
                var edx = e.Position.X - playerPos.X;
                var edz = e.Position.Z - playerPos.Z;

                var forward = edx * sinH + edz * cosH;
                var lateral = edx * cosH - edz * sinH;

                if (forward >= -e.HitboxRadius
                    && forward <= length + e.HitboxRadius
                    && MathF.Abs(lateral) <= halfWidth + e.HitboxRadius)
                    count++;
            }

            if (count > bestCount)
            {
                bestCount = count;
                bestAngle = aimAngle;
                bestTarget = candidate;
            }
        }

        return (bestTarget, bestCount, bestAngle);
    }

    private static float NormalizeAngle(float angle)
    {
        while (angle > MathF.PI) angle -= 2f * MathF.PI;
        while (angle < -MathF.PI) angle += 2f * MathF.PI;
        return angle;
    }
}
