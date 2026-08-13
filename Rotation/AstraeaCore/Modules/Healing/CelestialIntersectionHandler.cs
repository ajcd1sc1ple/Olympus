using System;
using Olympus.Config;
using Olympus.Data;
using Olympus.Models.Action;
using Olympus.Rotation.ApolloCore.Helpers;
using Olympus.Rotation.AstraeaCore.Abilities;
using Olympus.Rotation.AstraeaCore.Context;
using Olympus.Rotation.Common.Scheduling;
using Olympus.Services.Training;

namespace Olympus.Rotation.AstraeaCore.Modules.Healing;

public sealed class CelestialIntersectionHandler : IHealingHandler
{
    public int Priority => 15;
    public string Name => "CelestialIntersection";

    private static readonly string[] _alternatives =
    {
        "Essential Dignity (emergency heal)",
        "Aspected Benefic (GCD + regen)",
        "Save charge for tank damage",
    };

    public bool TryExecute(IAstraeaContext context, bool isMoving) => false;

    public void CollectCandidates(IAstraeaContext context, RotationScheduler scheduler, bool isMoving)
    {
        var config = context.Configuration.Astrologian;
        var player = context.Player;

        if (!config.EnableCelestialIntersection) return;
        if (player.Level < ASTActions.CelestialIntersection.MinLevel) return;
        if (!context.ActionService.IsActionReady(ASTActions.CelestialIntersection.ActionId)) return;

        var tankBusterImminent = TimelineHelper.IsTankBusterImminent(
            context.TimelineService, context.BossMechanicDetector, context.Configuration, out _);

        var target = tankBusterImminent
            ? TimelineHelper.ResolveTankBusterTarget(
                context.PartyHelper.FindTankInParty(player),
                context.PartyHelper.GetAllPartyMembers(player),
                player.EntityId)
            : context.PartyHelper.FindLowestHpPartyMember(player);
        if (target == null) return;
        if (context.HealingCoordination.IsTargetReserved(target.EntityId, context.PartyCoordinationService)) return;

        var hpPercent = context.PartyHelper.GetHpPercent(target);
        if (hpPercent > config.CelestialIntersectionThreshold && !tankBusterImminent) return;

        var action = ASTActions.CelestialIntersection;
        var capturedTarget = target;
        var capturedHpPercent = hpPercent;
        var capturedTb = tankBusterImminent;

        scheduler.PushOgcd(AstraeaAbilities.CelestialIntersection, target.GameObjectId, priority: Priority,
            onDispatched: _ =>
            {
                var healAmount = action.HealPotency * 10;
                context.HealingCoordination.TryReserveTarget(
                    capturedTarget.EntityId, context.PartyCoordinationService, healAmount, action.ActionId, 0);

                context.Debug.PlannedAction = action.Name;
                context.Debug.CelestialIntersectionState = capturedTb ? "TB Used" : "Used";

                if (context.TrainingService?.IsTrainingEnabled == true)
                {
                    var targetName = capturedTarget.Name?.TextValue ?? "Unknown";
                    var isTank = JobRegistry.IsTank(capturedTarget.ClassJob.RowId);
                    var shortReason = capturedTb
                        ? $"Celestial Intersection on {targetName} before tankbuster"
                        : $"Celestial Intersection on {targetName} at {capturedHpPercent:P0}";
                    var factors = new[]
                    {
                        $"Target HP: {capturedHpPercent:P0}",
                        $"Threshold: {config.CelestialIntersectionThreshold:P0}",
                        capturedTb ? "Tank buster imminent — tank shield dump" :
                            isTank ? "Tank target - will get shield" : "Non-tank - heal + regen",
                        "2 charges, 30s recharge",
                        "oGCD - weave without GCD clip",
                    };

                    context.TrainingService.RecordDecision(new ActionExplanation
                    {
                        Timestamp = DateTime.UtcNow,
                        ActionId = action.ActionId,
                        ActionName = "Celestial Intersection",
                        Category = "Healing",
                        TargetName = targetName,
                        ShortReason = shortReason,
                        DetailedReason = $"Celestial Intersection on {targetName} at {capturedHpPercent:P0} HP. {(capturedTb || isTank ? "Tank target receives 400 potency shield (great for tankbusters!)." : "Non-tank target receives 200 potency heal + 15s regen.")} 2 charges with 30s recharge - keep using them to maximize value!",
                        Factors = factors,
                        Alternatives = _alternatives,
                        Tip = "Celestial Intersection is excellent for tanks - the shield helps with auto-attacks and tankbusters. For non-tanks, it's a free oGCD heal + regen. Don't sit on charges!",
                        ConceptId = AstConcepts.CelestialIntersectionUsage,
                        Priority = capturedTb ? ExplanationPriority.High : ExplanationPriority.Normal,
                    });

                    context.TrainingService?.RecordConceptApplication(AstConcepts.CelestialIntersectionUsage, wasSuccessful: true, capturedTb || isTank ? "Tank shield applied" : "Heal and regen applied");
                }
            });
    }
}
