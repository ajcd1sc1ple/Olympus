using System;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Olympus.Compat;
using Olympus.Rotation.Common.Helpers;

namespace Olympus.Services.AutoAttack;

/// <summary>
/// Manages the game auto-attack toggle so it stays on until the engaged target dies.
/// Uses <see cref="AutoAttackState.Set"/> with a GeneralAction fallback (same toggle BossMod uses).
/// </summary>
public sealed unsafe class AutoAttackService : IAutoAttackService
{
    private const uint AutoAttackGeneralActionId = 1;

    private readonly Configuration _configuration;
    private readonly ITargetManager _targetManager;
    private readonly IObjectTable _objectTable;
    private readonly IPluginLog _log;

    private bool _isHoldingUntilDead;
    private ulong _engagedTargetId;
    private DateTime _lastToggleUtc = DateTime.MinValue;
    private static readonly TimeSpan ToggleCooldown = TimeSpan.FromMilliseconds(150);

    public AutoAttackService(
        Configuration configuration,
        ITargetManager targetManager,
        IObjectTable objectTable,
        IPluginLog log)
    {
        _configuration = configuration;
        _targetManager = targetManager;
        _objectTable = objectTable;
        _log = log;
    }

    /// <inheritdoc />
    public bool IsHoldingUntilDead => _isHoldingUntilDead;

    /// <inheritdoc />
    public ulong EngagedTargetId => _engagedTargetId;

    /// <inheritdoc />
    public bool ShouldTreatAsInCombat
    {
        get
        {
            if (!_configuration.EnableAutoAttackUntilDead)
                return false;

            var (engagedAlive, _) = EvaluateEngagedTarget();
            var hardTarget = GetHardTarget();
            var hasLivingHostile = hardTarget != null && IsLivingHostile(hardTarget);
            // While holding, also count a hard target that is alive/hostile even if
            // IsTargetable briefly flickers (common as enemies hit low HP).
            var hasHostileAliveHardTarget = hardTarget != null && IsHostileAlive(hardTarget);

            return AutoAttackHoldDecision.ShouldTreatAsInCombat(
                managementEnabled: true,
                isHoldingUntilDead: _isHoldingUntilDead,
                hasLivingHostileTarget: hasLivingHostile || (_isHoldingUntilDead && hasHostileAliveHardTarget),
                currentlyAutoAttacking: IsAutoAttacking(),
                engagedTargetStillAlive: engagedAlive);
        }
    }

    /// <inheritdoc />
    public void Update(bool inCombat)
    {
        if (!_configuration.EnableAutoAttackUntilDead || !_configuration.Enabled)
        {
            ClearHold();
            return;
        }

        var player = _objectTable.LocalPlayer;
        if (player == null || player.CurrentHp == 0)
        {
            ClearHold();
            return;
        }

        var standStillPunisher = PlayerSafetyHelper.IsStandStillPunisherActive(player);
        var hardTarget = GetHardTarget();
        var hasHostileAliveHardTarget = hardTarget != null && IsHostileAlive(hardTarget);
        var hasLivingHostile = hardTarget != null && IsLivingHostile(hardTarget);
        var targetIsDead = hardTarget != null && IsHostile(hardTarget) && IsDead(hardTarget);
        var currentlyAa = IsAutoAttacking();

        if (hasHostileAliveHardTarget && hardTarget != null)
            _engagedTargetId = hardTarget.GameObjectId;

        var (engagedAlive, engagedDied) = EvaluateEngagedTarget();

        // While already holding, keep treating a briefly untargetable living hard target
        // as valid so we do not drop AA / combat at low HP.
        var livingForHold = hasLivingHostile
                            || (_isHoldingUntilDead && hasHostileAliveHardTarget);

        _isHoldingUntilDead = AutoAttackHoldDecision.ShouldHold(
            currentlyHolding: _isHoldingUntilDead,
            inCombat: inCombat,
            currentlyAutoAttacking: currentlyAa,
            hasLivingHostileTarget: livingForHold,
            engagedTargetStillAlive: engagedAlive,
            targetIsDead: targetIsDead,
            engagedTargetDied: engagedDied,
            standStillPunisherActive: standStillPunisher,
            pluginEnabled: _configuration.Enabled,
            playerAlive: true);

        if (!_isHoldingUntilDead && (targetIsDead || engagedDied))
            _engagedTargetId = 0;

        var desired = AutoAttackHoldDecision.GetDesiredState(
            managementEnabled: true,
            pluginEnabled: true,
            playerAlive: true,
            inCombat: inCombat,
            currentlyAutoAttacking: currentlyAa,
            hasLivingHostileTarget: livingForHold,
            targetIsDead: targetIsDead,
            engagedTargetStillAlive: engagedAlive,
            engagedTargetDied: engagedDied,
            isHoldingUntilDead: _isHoldingUntilDead,
            standStillPunisherActive: standStillPunisher);

        if (desired is null || desired.Value == currentlyAa)
            return;

        ApplyAutoAttack(desired.Value);
    }

    private (bool stillAlive, bool died) EvaluateEngagedTarget()
    {
        if (_engagedTargetId == 0)
            return (false, false);

        var obj = _objectTable.SearchById(_engagedTargetId);
        if (obj is not IBattleNpc enemy)
        {
            // Despawned after death — treat as died so we release the hold.
            return (false, _isHoldingUntilDead);
        }

        if (IsDead(enemy))
            return (false, true);

        // Still alive as long as hostile and not dead — do not require IsTargetable
        // (targetable flickers are common at low HP / death animations).
        return (IsHostile(enemy), false);
    }

    private IBattleNpc? GetHardTarget() => _targetManager.Target as IBattleNpc;

    private static bool IsHostile(IBattleNpc enemy) =>
        (byte)enemy.BattleNpcKind == BattleNpcKinds.Combatant || enemy.SubKind == 0;

    private static bool IsDead(IBattleNpc enemy) =>
        enemy.IsDead || enemy.CurrentHp == 0;

    private static bool IsHostileAlive(IBattleNpc enemy) =>
        IsHostile(enemy) && !IsDead(enemy);

    private static bool IsLivingHostile(IBattleNpc enemy) =>
        IsHostileAlive(enemy) && enemy.IsTargetable;

    private void ClearHold()
    {
        _isHoldingUntilDead = false;
        _engagedTargetId = 0;
    }

    private static bool IsAutoAttacking()
    {
        try
        {
            var uiState = UIState.Instance();
            if (uiState == null)
                return false;
            return uiState->WeaponState.AutoAttackState.IsAutoAttacking;
        }
        catch
        {
            return false;
        }
    }

    private void ApplyAutoAttack(bool enable)
    {
        var now = DateTime.UtcNow;
        if (now - _lastToggleUtc < ToggleCooldown)
            return;

        try
        {
            var uiState = UIState.Instance();
            if (uiState != null)
            {
                // Prefer Set — validates and updates the global auto-attack state.
                // Some plugins hook Set and may report success without changing state
                // (e.g. BossMod preventing early autos); verify before treating as done.
                uiState->WeaponState.AutoAttackState.Set(enable);
                if (IsAutoAttacking() == enable)
                {
                    _lastToggleUtc = now;
                    return;
                }
            }

            // Fallback: General Action 1 is the Auto-Attack toggle (BossMod uses this path).
            var am = ActionManager.Instance();
            if (am != null && am->UseAction(ActionType.GeneralAction, AutoAttackGeneralActionId))
                _lastToggleUtc = now;
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "[AutoAttack] Failed to set auto-attack to {0}", enable);
        }
    }
}
