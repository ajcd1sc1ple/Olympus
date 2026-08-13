using System;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Olympus.Config;
using Olympus.Data;
using Olympus.Models.Action;
using Olympus.Rotation.ApolloCore.Helpers;
using Olympus.Rotation.AthenaCore.Abilities;
using Olympus.Rotation.AthenaCore.Context;
using Olympus.Rotation.Common.Helpers;
using Olympus.Rotation.Common.Scheduling;
using static Olympus.Rotation.Common.Scheduling.HealerSchedulerPriorities;
using Olympus.Services.Training;

namespace Olympus.Rotation.AthenaCore.Modules.Healing;

/// <summary>
/// Adloquium / Manifestation / Physick.
/// On timeline tank-busters, forces Adlo/Manifestation on the tank even at full HP
/// so Galvanize is applied before impact.
/// </summary>
public sealed class SingleTargetHealHandler : IHealingHandler
{
    public int Priority => 20;
    public string Name => "SingleTargetHeal";

    public void CollectCandidates(IAthenaContext context, RotationScheduler scheduler, bool isMoving)
    {
        if (isMoving) return;

        var config = context.Configuration.Scholar;
        var player = context.Player;

        if (!config.EnableAdloquium && !config.EnablePhysick) return;

        var tankBusterImminent = TimelineHelper.IsTankBusterImminentForGcdHealPrep(
            context.TimelineService, context.BossMechanicDetector, context.Configuration, out _);

        if (tankBusterImminent && config.EnableAdloquium)
        {
            // Max shield dump on the tank before the buster — ignore HP thresholds.
            // Only push when Galvanize is missing; otherwise fall through so DPS can run.
            var tank = TimelineHelper.ResolveTankBusterTarget(
                context.PartyHelper.FindTankInParty(player),
                context.PartyHelper.GetAllPartyMembers(player),
                player.EntityId);
            if (tank != null
                && !context.HealingCoordination.IsTargetReserved(tank.EntityId, context.PartyCoordinationService)
                && !context.StatusHelper.HasGalvanize(tank)
                && !(config.AvoidOverwritingSageShields && HasSageShield(context, tank)))
            {
                var tbHp = context.PartyHelper.GetHpPercent(tank);
                if (TrySelectAdlo(context, config, player, tank, out var tbAction, out var tbBehavior))
                {
                    PushHeal(context, scheduler, config, tbAction, tbBehavior, tank, tbHp, tankBusterImminent: true,
                        Mitigation(true, timelineOffset: TimelineTankBusterOffset, reactivePriority: Priority));
                    return;
                }
            }
        }

        var target = context.Configuration.Healing.UseDamageIntakeTriage
            ? context.PartyHelper.FindMostEndangeredPartyMember(
                player, context.DamageIntakeService, 0, context.DamageTrendService, context.ShieldTrackingService)
            : context.PartyHelper.FindLowestHpPartyMember(player);
        if (target == null) return;
        if (context.HealingCoordination.IsTargetReserved(target.EntityId, context.PartyCoordinationService)) return;

        var hpPercent = context.PartyHelper.GetHpPercent(target);

        ActionDefinition? action = null;
        AbilityBehavior? behavior = null;

        if (config.EnableAdloquium && hpPercent <= config.AdloquiumThreshold &&
            TrySelectAdlo(context, config, player, target, out var adloAction, out var adloBehavior))
        {
            action = adloAction;
            behavior = adloBehavior;
        }

        if (action == null && config.EnablePhysick && hpPercent <= config.PhysickThreshold)
        {
            action = SCHActions.Physick;
            behavior = AthenaAbilities.Physick;
        }

        if (action == null || behavior == null) return;

        if (action.ActionId == SCHActions.Physick.ActionId &&
            CoHealerAwarenessHelper.CoHealerWillCover(
                context.Configuration.Healing.EnableCoHealerAwareness,
                context.CoHealerDetectionService,
                target,
                context.Configuration.Healing.CoHealerPendingHealThreshold))
            return;

        PushHeal(context, scheduler, config, action, behavior, target, hpPercent, tankBusterImminent: false, Priority);
    }

    private static bool TrySelectAdlo(
        IAthenaContext context,
        ScholarConfig config,
        IPlayerCharacter player,
        IBattleChara target,
        out ActionDefinition action,
        out AbilityBehavior behavior)
    {
        action = null!;
        behavior = null!;

        if (context.FairyStateManager.IsSeraphOrSeraphismActive && player.Level >= SCHActions.Manifestation.MinLevel)
        {
            if (config.AvoidOverwritingSageShields && HasSageShield(context, target))
                return false;
            action = SCHActions.Manifestation;
            behavior = AthenaAbilities.Manifestation;
            return true;
        }

        if (player.Level < SCHActions.Adloquium.MinLevel)
            return false;
        if (context.StatusHelper.HasGalvanize(target))
            return false;
        if (config.AvoidOverwritingSageShields && HasSageShield(context, target))
            return false;

        action = SCHActions.Adloquium;
        behavior = AthenaAbilities.Adloquium;
        return true;
    }

    private static void PushHeal(
        IAthenaContext context,
        RotationScheduler scheduler,
        ScholarConfig config,
        ActionDefinition action,
        AbilityBehavior behavior,
        IBattleChara target,
        float hpPercent,
        bool tankBusterImminent,
        int priority)
    {
        var capturedAction = action;
        var capturedTarget = target;
        var capturedHpPercent = hpPercent;
        var capturedTb = tankBusterImminent;

        scheduler.PushGcd(behavior, target.GameObjectId, priority: priority,
            onDispatched: _ =>
            {
                var healAmount = capturedAction.HealPotency * 10;
                var castTimeMs = (int)(capturedAction.CastTime * 1000);
                context.HealingCoordination.TryReserveTarget(
                    capturedTarget.EntityId, context.PartyCoordinationService, healAmount, capturedAction.ActionId, castTimeMs);

                context.Debug.PlannedAction = capturedAction.Name;
                context.Debug.PlanningState = capturedTb ? "TB Adlo" : "Single Heal";

                if (context.TrainingService?.IsTrainingEnabled == true)
                {
                    var targetName = capturedTarget.Name?.TextValue ?? "Unknown";
                    var isAdlo = capturedAction.ActionId == SCHActions.Adloquium.ActionId ||
                                 capturedAction.ActionId == SCHActions.Manifestation.ActionId;
                    var isPhysick = capturedAction.ActionId == SCHActions.Physick.ActionId;

                    string shortReason;
                    string[] factors;
                    string tip;
                    string conceptId;

                    if (isAdlo)
                    {
                        shortReason = capturedTb
                            ? $"{capturedAction.Name} on {targetName} before tankbuster"
                            : $"{capturedAction.Name} on {targetName} at {capturedHpPercent:P0}";
                        factors = new[]
                        {
                            $"Target HP: {capturedHpPercent:P0}",
                            $"Threshold: {config.AdloquiumThreshold:P0}",
                            capturedTb ? "Tank buster imminent — max shield prep" : "Provides heal + Galvanize shield",
                            "Shield can crit for Catalyze bonus",
                            capturedTb ? "Applied regardless of HP" : "Target had no existing shield",
                        };
                        tip = "Adloquium is your primary single-target GCD heal. The shield is valuable before damage. Critical Adlos create massive shields with Catalyze!";
                        conceptId = SchConcepts.AdloquiumUsage;
                    }
                    else
                    {
                        shortReason = $"Physick on {targetName} at {capturedHpPercent:P0}";
                        factors = new[]
                        {
                            $"Target HP: {capturedHpPercent:P0}",
                            $"Threshold: {config.PhysickThreshold:P0}",
                            "Pure healing (no shield)",
                            "Low MP cost",
                            "Used when shield not needed/available",
                        };
                        tip = "Physick is generally weak. Use Adloquium for shields or oGCDs like Lustrate when possible. Physick is a last resort.";
                        conceptId = SchConcepts.AdloquiumUsage;
                    }

                    var alternatives = new[]
                    {
                        "Lustrate (oGCD, uses Aetherflow)",
                        "Excogitation (proactive)",
                        isPhysick ? "Adloquium (adds shield)" : "Physick (no shield needed)",
                    };

                    context.TrainingService.RecordDecision(new ActionExplanation
                    {
                        Timestamp = DateTime.UtcNow,
                        ActionId = capturedAction.ActionId,
                        ActionName = capturedAction.Name,
                        Category = "Healing",
                        TargetName = targetName,
                        ShortReason = shortReason,
                        DetailedReason = $"{capturedAction.Name} on {targetName} at {capturedHpPercent:P0} HP. {(isAdlo ? "Adloquium provides 300 potency heal plus a 540 potency Galvanize shield (or 810 with crit Catalyze). " : "Physick provides 450 potency heal but no shield. It's SCH's weakest GCD heal option. ")}{(capturedTb ? "Used proactively before predicted tank buster. " : "")}GCD heals should be used sparingly - prefer oGCD heals when available.",
                        Factors = factors,
                        Alternatives = alternatives,
                        Tip = tip,
                        ConceptId = conceptId,
                        Priority = capturedTb || capturedHpPercent < 0.3f
                            ? ExplanationPriority.High
                            : ExplanationPriority.Normal,
                    });

                    context.TrainingService.RecordConceptApplication(conceptId, wasSuccessful: true);
                }
            });
    }

    private static bool HasSageShield(IAthenaContext context, IBattleChara target)
    {
        const ushort EukrasianDiagnosisStatusId = 2607;
        const ushort EukrasianPrognosisStatusId = 2609;

        if (target.StatusList == null) return false;

        foreach (var status in target.StatusList)
        {
            if (status.StatusId == EukrasianDiagnosisStatusId ||
                status.StatusId == EukrasianPrognosisStatusId)
                return true;
        }
        return false;
    }
}
