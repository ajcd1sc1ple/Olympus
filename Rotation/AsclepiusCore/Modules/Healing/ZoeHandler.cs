using System;
using Olympus.Config;
using Olympus.Data;
using Olympus.Rotation.AsclepiusCore.Abilities;
using Olympus.Rotation.AsclepiusCore.Context;
using Olympus.Rotation.Common.Scheduling;
using Olympus.Services.Training;

namespace Olympus.Rotation.AsclepiusCore.Modules.Healing;

public sealed class ZoeHandler : IHealingHandler
{
    public int Priority => 60;
    public string Name => "Zoe";

    public void CollectCandidates(IAsclepiusContext context, RotationScheduler scheduler, bool isMoving)
    {
        var config = context.Configuration.Sage;
        var player = context.Player;

        if (!config.EnableZoe) return;
        if (config.ZoeStrategy == ZoeUsageStrategy.Manual)
        {
            context.Debug.ZoeState = "Manual";
            return;
        }

        if (player.Level < SGEActions.Zoe.MinLevel) return;
        if (context.HasZoe) { context.Debug.ZoeState = "Active"; return; }
        if (!context.ActionService.IsActionReady(SGEActions.Zoe.ActionId)) { context.Debug.ZoeState = "On CD"; return; }

        var (avgHp, lowestHp, injuredCount) = context.PartyHelper.CalculatePartyHealthMetrics(player);
        if (!ShouldArmZoe(config, avgHp, lowestHp, injuredCount, context))
            return;

        var capturedLowestHp = lowestHp;
        var action = SGEActions.Zoe;

        scheduler.PushOgcd(AsclepiusAbilities.Zoe, player.GameObjectId, priority: Priority,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = action.Name;
                context.Debug.PlanningState = "Zoe";
                context.Debug.ZoeState = "Executing";

                if (context.TrainingService?.IsTrainingEnabled == true)
                {
                    context.TrainingService.RecordDecision(new ActionExplanation
                    {
                        Timestamp = DateTime.UtcNow,
                        ActionId = action.ActionId,
                        ActionName = "Zoe",
                        Category = "Healing",
                        TargetName = "Self (buff)",
                        ShortReason = $"Zoe - preparing 50% boosted GCD heal (lowest: {capturedLowestHp:P0})",
                        DetailedReason = $"Zoe activated to boost the next GCD heal by 50%. Party member at {capturedLowestHp:P0} HP - the boosted heal will provide much more recovery. Zoe works on Diagnosis, Prognosis, Pneuma, and Eukrasian heals!",
                        Factors = new[]
                        {
                            $"Lowest HP: {capturedLowestHp:P0}",
                            $"Strategy: {config.ZoeStrategy}",
                            "50% potency boost on next GCD heal",
                            "90s cooldown",
                            "Works on: Diagnosis, Prognosis, Pneuma, E.Diagnosis, E.Prognosis",
                        },
                        Alternatives = new[]
                        {
                            "Krasis (20% healing received buff)",
                            "Direct heal without buff",
                            "oGCD heals instead",
                        },
                        Tip = "Zoe is a 50% boost to your next GCD heal! Best paired with Pneuma (600 potency → 900 potency party heal!) or E.Prognosis for massive party shields. Don't waste it on small heals!",
                        ConceptId = SgeConcepts.ZoeUsage,
                        Priority = ExplanationPriority.Normal,
                    });
                }
            });
    }

    private static bool ShouldArmZoe(
        SageConfig config,
        float avgHp,
        float lowestHp,
        int injuredCount,
        IAsclepiusContext context)
    {
        switch (config.ZoeStrategy)
        {
            case ZoeUsageStrategy.WithPneuma:
                if (!config.EnablePneuma)
                {
                    context.Debug.ZoeState = "Waiting for Pneuma (disabled)";
                    return false;
                }

                if (context.Player.Level < SGEActions.Pneuma.MinLevel)
                {
                    context.Debug.ZoeState = "Waiting for Pneuma (level)";
                    return false;
                }

                if (!context.ActionService.IsActionReady(SGEActions.Pneuma.ActionId))
                {
                    context.Debug.ZoeState = "Waiting for Pneuma CD";
                    return false;
                }

                if (avgHp > config.PneumaThreshold || injuredCount < config.AoEHealMinTargets)
                {
                    context.Debug.ZoeState = $"Waiting for Pneuma window (avg {avgHp:P0})";
                    return false;
                }

                return true;

            case ZoeUsageStrategy.WithEukrasianPrognosis:
                if (!config.EnableEukrasianPrognosis)
                {
                    context.Debug.ZoeState = "Waiting for E.Prognosis (disabled)";
                    return false;
                }

                if (injuredCount < config.AoEHealMinTargets || avgHp > config.AoEHealThreshold)
                {
                    context.Debug.ZoeState = $"Waiting for E.Prognosis window (avg {avgHp:P0})";
                    return false;
                }

                return true;

            case ZoeUsageStrategy.OnDemand:
            default:
                if (lowestHp > config.DiagnosisThreshold)
                {
                    context.Debug.ZoeState = $"Lowest HP {lowestHp:P0}";
                    return false;
                }

                return true;
        }
    }
}
