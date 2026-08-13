using System;
using Olympus.Config;
using Olympus.Data;
using Olympus.Rotation.AsclepiusCore.Abilities;
using Olympus.Rotation.AsclepiusCore.Context;
using Olympus.Rotation.Common.Scheduling;
using Olympus.Services.Training;

namespace Olympus.Rotation.AsclepiusCore.Modules.Healing;

public sealed class RhizomataHandler : IHealingHandler
{
    public int Priority => 50;
    public string Name => "Rhizomata";

    public void CollectCandidates(IAsclepiusContext context, RotationScheduler scheduler, bool isMoving)
    {
        var config = context.Configuration.Sage;
        var player = context.Player;

        if (!config.EnableRhizomata) return;
        if (player.Level < SGEActions.Rhizomata.MinLevel) return;
        if (!context.ActionService.IsActionReady(SGEActions.Rhizomata.ActionId)) { context.Debug.RhizomataState = "On CD"; return; }
        if (context.AddersgallStacks >= 3) { context.Debug.RhizomataState = "At max stacks"; return; }

        var freeSlots = 3 - context.AddersgallStacks;
        var action = SGEActions.Rhizomata;
        var capturedStacks = context.AddersgallStacks;
        var capturedTimer = context.AddersgallTimer;

        // Use Rhizomata before natural regen fills the last slot (banks the free stack).
        if (config.PreventAddersgallCap
            && context.AddersgallStacks >= 2
            && context.AddersgallTimer < config.AddersgallCapPreventWindow)
        {
            PushRhizomata(context, scheduler, action, capturedStacks, capturedTimer, "Preventing cap",
                $"Rhizomata - preventing Addersgall cap ({capturedStacks}/3, {capturedTimer:F1}s)",
                $"Rhizomata used to prevent Addersgall overcap. Currently at {capturedStacks}/3 stacks with {capturedTimer:F1}s until next natural regen. Using Rhizomata now banks an extra stack that won't be lost.",
                ExplanationPriority.Normal);
            return;
        }

        // Normal / emergency generation: require configured free slots (stacks == 0 always qualifies for default min=2).
        if (freeSlots < config.RhizomataMinFreeSlots)
        {
            context.Debug.RhizomataState = $"Need {config.RhizomataMinFreeSlots} free slots ({freeSlots} free)";
            return;
        }

        if (context.AddersgallStacks == 0)
        {
            PushRhizomata(context, scheduler, action, capturedStacks, capturedTimer, "Out of Addersgall",
                "Rhizomata - out of Addersgall!",
                "Rhizomata used because Addersgall is empty. This provides an immediate stack for emergency healing options like Druochole, Taurochole, Ixochole, or Kerachole.",
                ExplanationPriority.High);
            return;
        }

        // Free-slot gate passed with partial stacks — bank a stack proactively.
        PushRhizomata(context, scheduler, action, capturedStacks, capturedTimer, "Free slots",
            $"Rhizomata - banking stack ({capturedStacks}/3, {freeSlots} free)",
            $"Rhizomata used with {freeSlots} free Addersgall slots (min {config.RhizomataMinFreeSlots}). Banking a free stack before the gauge fills.",
            ExplanationPriority.Normal);
    }

    private void PushRhizomata(
        IAsclepiusContext context,
        RotationScheduler scheduler,
        Olympus.Models.Action.ActionDefinition action,
        int capturedStacks,
        float capturedTimer,
        string state,
        string shortReason,
        string detailedReason,
        ExplanationPriority priority)
    {
        scheduler.PushOgcd(AsclepiusAbilities.Rhizomata, context.Player.GameObjectId, priority: Priority,
            onDispatched: _ =>
            {
                context.Debug.PlannedAction = action.Name;
                context.Debug.PlanningState = "Rhizomata";
                context.Debug.RhizomataState = state;

                if (context.TrainingService?.IsTrainingEnabled == true)
                {
                    context.TrainingService.RecordDecision(new ActionExplanation
                    {
                        Timestamp = DateTime.UtcNow,
                        ActionId = action.ActionId,
                        ActionName = "Rhizomata",
                        Category = "Resource",
                        TargetName = "Self",
                        ShortReason = shortReason,
                        DetailedReason = detailedReason,
                        Factors = new[]
                        {
                            $"Current stacks: {capturedStacks}/3",
                            $"Timer to next regen: {capturedTimer:F1}s",
                            "90s cooldown",
                        },
                        Alternatives = new[]
                        {
                            "Spend Addersgall first (Druochole, Kerachole, etc.)",
                            "Wait for natural regen",
                        },
                        Tip = "Rhizomata grants a free Addersgall stack on a 90s CD. Use it when you have free slots, or when you're empty and need healing resources!",
                        ConceptId = SgeConcepts.RhizomataUsage,
                        Priority = priority,
                    });
                }
            });
    }
}
