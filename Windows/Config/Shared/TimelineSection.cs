using System;
using Dalamud.Bindings.ImGui;
using Olympus.Localization;

namespace Olympus.Windows.Config.Shared;

/// <summary>
/// Renders the Timeline Integration settings section, shared across all roles.
/// </summary>
public sealed class TimelineSection
{
    private readonly Configuration config;
    private readonly Action save;

    public TimelineSection(Configuration config, Action save)
    {
        this.config = config;
        this.save = save;
    }

    public void Draw()
    {
        ImGui.TextColored(new System.Numerics.Vector4(0.6f, 0.8f, 1.0f, 1.0f),
            Loc.T(LocalizedStrings.Timeline.SectionHeader, "Timeline Integration"));
        ImGui.Separator();
        ConfigUIHelpers.Spacing();

        ConfigUIHelpers.Toggle(
            Loc.T(LocalizedStrings.Timeline.EnablePredictions, "Enable Timeline Predictions"),
            () => config.Timeline.EnableTimelinePredictions,
            v => config.Timeline.EnableTimelinePredictions = v,
            Loc.T(LocalizedStrings.Timeline.EnablePredictionsDesc,
                "Use fight timelines for precise mechanic timing (DSU/TOP/FRU and any future zones)."),
            save);

        if (!config.Timeline.EnableTimelinePredictions)
            return;

        ConfigUIHelpers.BeginIndent();

        ConfigUIHelpers.Toggle(
            Loc.T(LocalizedStrings.Timeline.EnableBossModIntegration, "Use BossMod timeline"),
            () => config.Timeline.EnableBossModTimelineIntegration,
            v => config.Timeline.EnableBossModTimelineIntegration = v,
            Loc.T(LocalizedStrings.Timeline.EnableBossModIntegrationDesc,
                "When BossMod Reborn has an active encounter module, use cast-hint timing when available, otherwise BossMod.Timeline. Embedded Cactbot timelines are fallback only. Untargetable holds stay Cactbot-only."),
            save);

        ConfigUIHelpers.Spacing();

        config.Timeline.TimelineConfidenceThreshold = ConfigUIHelpers.ThresholdSliderSmall(
            Loc.T(LocalizedStrings.Timeline.ConfidenceThreshold, "Confidence Threshold"),
            config.Timeline.TimelineConfidenceThreshold, 50f, 100f,
            Loc.T(LocalizedStrings.Timeline.ConfidenceThresholdDesc,
                "Minimum confidence required before trusting timeline predictions. Higher = more conservative."),
            save,
            v => config.Timeline.TimelineConfidenceThreshold = v);

        ConfigUIHelpers.Toggle(
            Loc.T(LocalizedStrings.Timeline.EnableMechanicAwareCasting, "Block Casts Before Mechanics"),
            () => config.Timeline.EnableMechanicAwareCasting,
            v => config.Timeline.EnableMechanicAwareCasting = v,
            Loc.T(LocalizedStrings.Timeline.EnableMechanicAwareCastingDesc,
                "Deprecated: casting is no longer blocked before raidwides/tankbusters (caused GCD downtime). Timeline tank-buster mitigations still fire normally."),
            save);

        ConfigUIHelpers.EndIndent();
    }
}
