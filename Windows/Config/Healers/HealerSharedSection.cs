using System;
using Dalamud.Bindings.ImGui;
using Olympus.Config;
using Olympus.Data;
using Olympus.Localization;

namespace Olympus.Windows.Config.Healers;

/// <summary>
/// Renders settings that apply to all healer jobs (WHM/SCH/AST/SGE).
/// </summary>
public sealed class HealerSharedSection
{
    private readonly Configuration config;
    private readonly Action save;

    public HealerSharedSection(Configuration config, Action save)
    {
        this.config = config;
        this.save = save;
    }

    public void Draw()
    {
        ImGui.TextColored(new System.Numerics.Vector4(0.8f, 0.9f, 0.8f, 1f),
            Loc.T(LocalizedStrings.HealerShared.Header, "Shared Healer Settings"));
        ImGui.TextDisabled(Loc.T(LocalizedStrings.HealerShared.Description,
            "These settings apply to all healer jobs."));
        ConfigUIHelpers.Spacing();

        DrawMpManagement();
        DrawBurstWindow();
        DrawTriageSection();
        DrawCoHealerSection();
        DrawPredictionAndAwareness();
        DrawTimelineIntegration();
    }

    private void DrawTriageSection()
    {
        if (ConfigUIHelpers.SectionHeader(Loc.T(LocalizedStrings.HealerShared.TriageSection, "Healing Triage"), "Healer"))
        {
            ConfigUIHelpers.BeginIndent();

            ConfigUIHelpers.Toggle(
                Loc.T(LocalizedStrings.HealerShared.UseDamageBasedTriage, "Use Damage-Based Triage"),
                () => config.Healing.UseDamageIntakeTriage,
                v => config.Healing.UseDamageIntakeTriage = v,
                Loc.T(LocalizedStrings.HealerShared.UseDamageBasedTriageDesc, "Prioritize healing targets taking active damage."),
                save);

            if (config.Healing.UseDamageIntakeTriage)
            {
                ConfigUIHelpers.BeginIndent();
                var presetNames = Enum.GetNames<TriagePreset>();
                var currentPreset = (int)config.Healing.TriagePreset;
                ImGui.SetNextItemWidth(150);
                if (ImGui.Combo(Loc.T(LocalizedStrings.HealerShared.TriagePreset, "Triage Preset"), ref currentPreset, presetNames, presetNames.Length))
                {
                    config.Healing.TriagePreset = (TriagePreset)currentPreset;
                    save();
                }

                var presetDesc = config.Healing.TriagePreset switch
                {
                    TriagePreset.Balanced => Loc.T(LocalizedStrings.HealerShared.TriagePresetBalanced, "Balanced weights across all factors"),
                    TriagePreset.TankFocus => Loc.T(LocalizedStrings.HealerShared.TriagePresetTankFocus, "Prioritize tanks over DPS"),
                    TriagePreset.SpreadDamage => Loc.T(LocalizedStrings.HealerShared.TriagePresetSpreadDamage, "React to highest damage intake"),
                    TriagePreset.RaidWide => Loc.T(LocalizedStrings.HealerShared.TriagePresetRaidWide, "Focus on lowest HP members"),
                    TriagePreset.Custom => Loc.T(LocalizedStrings.HealerShared.TriagePresetCustom, "Use custom weight values below"),
                    _ => ""
                };
                ImGui.TextDisabled(presetDesc);

                if (config.Healing.TriagePreset == TriagePreset.Custom)
                {
                    config.Healing.CustomTriageWeights.DamageRate = ConfigUIHelpers.ThresholdSliderSmall(
                        "Damage Rate", config.Healing.CustomTriageWeights.DamageRate, 0f, 60f, null, save, v => config.Healing.CustomTriageWeights.DamageRate = v);
                    config.Healing.CustomTriageWeights.TankBonus = ConfigUIHelpers.ThresholdSliderSmall(
                        "Tank Bonus", config.Healing.CustomTriageWeights.TankBonus, 0f, 60f, null, save, v => config.Healing.CustomTriageWeights.TankBonus = v);
                    config.Healing.CustomTriageWeights.MissingHp = ConfigUIHelpers.ThresholdSliderSmall(
                        "Missing HP", config.Healing.CustomTriageWeights.MissingHp, 0f, 60f, null, save, v => config.Healing.CustomTriageWeights.MissingHp = v);
                    config.Healing.CustomTriageWeights.DamageAcceleration = ConfigUIHelpers.ThresholdSliderSmall(
                        "Acceleration", config.Healing.CustomTriageWeights.DamageAcceleration, 0f, 30f, null, save, v => config.Healing.CustomTriageWeights.DamageAcceleration = v);
                }
                ConfigUIHelpers.EndIndent();
            }

            ConfigUIHelpers.EndIndent();
        }
    }

    private void DrawCoHealerSection()
    {
        if (ConfigUIHelpers.SectionHeader(Loc.T(LocalizedStrings.HealerShared.CoHealerSection, "Co-Healer Awareness"), "Healer"))
        {
            ConfigUIHelpers.BeginIndent();

            ConfigUIHelpers.Toggle(
                Loc.T(LocalizedStrings.HealerShared.EnableCoHealerAwareness, "Enable Co-Healer Awareness"),
                () => config.Healing.EnableCoHealerAwareness,
                v => config.Healing.EnableCoHealerAwareness = v,
                Loc.T(LocalizedStrings.HealerShared.EnableCoHealerAwarenessDesc,
                    "Skip redundant heals when another healer already has a heal incoming on the target."),
                save);

            if (config.Healing.EnableCoHealerAwareness)
            {
                ConfigUIHelpers.BeginIndent();
                config.Healing.CoHealerActiveWindow = ConfigUIHelpers.FloatSlider(
                    Loc.T(LocalizedStrings.HealerShared.CoHealerActiveWindow, "Active Window (sec)"),
                    config.Healing.CoHealerActiveWindow, 3f, 30f, "%.0f",
                    Loc.T(LocalizedStrings.HealerShared.CoHealerActiveWindowDesc,
                        "Treat the co-healer as active if they healed within this many seconds."),
                    save,
                    v => config.Healing.CoHealerActiveWindow = v);

                config.Healing.CoHealerPendingHealThreshold = ConfigUIHelpers.ThresholdSlider(
                    Loc.T(LocalizedStrings.HealerShared.CoHealerPendingHealThreshold, "Pending Heal Cover"),
                    config.Healing.CoHealerPendingHealThreshold, 30f, 100f,
                    Loc.T(LocalizedStrings.HealerShared.CoHealerPendingHealThresholdDesc,
                        "Skip healing when the co-healer's pending heal covers at least this much of missing HP."),
                    save,
                    v => config.Healing.CoHealerPendingHealThreshold = v);
                ConfigUIHelpers.EndIndent();
            }

            ConfigUIHelpers.EndIndent();
        }
    }

    private void DrawMpManagement()
    {
        if (ConfigUIHelpers.SectionHeader(Loc.T(LocalizedStrings.HealerShared.MpManagement, "MP Management"), "Healer"))
        {
            ConfigUIHelpers.BeginIndent();

            ConfigUIHelpers.Toggle(
                Loc.T(LocalizedStrings.HealerShared.EnableLucidDreaming, "Enable Lucid Dreaming"),
                () => config.HealerShared.EnableLucidDreaming,
                v => config.HealerShared.EnableLucidDreaming = v,
                null, save,
                actionId: RoleActions.LucidDreaming.ActionId);

            if (config.HealerShared.EnableLucidDreaming)
            {
                config.HealerShared.LucidDreamingThreshold = ConfigUIHelpers.ThresholdSlider(
                    Loc.T(LocalizedStrings.HealerShared.LucidMpThreshold, "Lucid MP Threshold"),
                    config.HealerShared.LucidDreamingThreshold, 40f, 90f,
                    Loc.T(LocalizedStrings.HealerShared.LucidMpThresholdDesc,
                        "Fire Lucid Dreaming when MP drops below this percentage. White Mage uses its own predictive MP forecast and ignores this slider."),
                    save,
                    v => config.HealerShared.LucidDreamingThreshold = v);
            }

            ConfigUIHelpers.EndIndent();
        }
    }

    private void DrawPredictionAndAwareness()
    {
        if (ConfigUIHelpers.SectionHeader(Loc.T(LocalizedStrings.HealerShared.PredictionSection, "Prediction & Awareness"), "Healer"))
        {
            ConfigUIHelpers.BeginIndent();

            ConfigUIHelpers.Toggle(
                Loc.T(LocalizedStrings.HealerShared.EnableMechanicAwareness, "Enable Mechanic Awareness"),
                () => config.Healing.EnableMechanicAwareness,
                v => config.Healing.EnableMechanicAwareness = v,
                Loc.T(LocalizedStrings.HealerShared.EnableMechanicAwarenessDesc,
                    "Detect raidwide and tank buster patterns from damage intake to pre-arm shields and mitigation."),
                save);

            ConfigUIHelpers.Toggle(
                Loc.T(LocalizedStrings.HealerShared.EnableCritVarianceReduction, "Account for Crit Variance"),
                () => config.Healing.EnableCritVarianceReduction,
                v => config.Healing.EnableCritVarianceReduction = v,
                Loc.T(LocalizedStrings.HealerShared.EnableCritVarianceReductionDesc,
                    "Discount pending heals by expected crit-roll variance so overheal prediction stays conservative."),
                save);

            ConfigUIHelpers.Toggle(
                Loc.T(LocalizedStrings.HealerShared.EnableSurvivabilityTrending, "Weight Survivability Trend"),
                () => config.Healing.EnableSurvivabilityTrending,
                v => config.Healing.EnableSurvivabilityTrending = v,
                Loc.T(LocalizedStrings.HealerShared.EnableSurvivabilityTrendingDesc,
                    "Favor stronger heals when party HP is trending down rapidly (scored selection only)."),
                save);

            ConfigUIHelpers.EndIndent();
        }
    }

    private void DrawTimelineIntegration()
    {
        if (ConfigUIHelpers.SectionHeader(Loc.T(LocalizedStrings.HealerShared.TimelineSection, "Timeline Integration"), "Healer"))
        {
            ConfigUIHelpers.BeginIndent();

            ImGui.TextDisabled(Loc.T(LocalizedStrings.HealerShared.TimelineMasterMoved,
                "Timeline master toggles now live in Shared > Timeline Integration."));
            ImGui.Spacing();

            if (config.Timeline.EnableTimelinePredictions)
            {
                config.Healing.RaidwidePreparationWindow = ConfigUIHelpers.FloatSlider(
                    Loc.T(LocalizedStrings.HealerShared.RaidwideWindow, "Raidwide Window (sec)"),
                    config.Healing.RaidwidePreparationWindow, 2f, 10f, "%.1f",
                    "Seconds before a predicted raidwide to start preparing shields and mitigation.",
                    save,
                    v => config.Healing.RaidwidePreparationWindow = v);

                config.Healing.TankBusterPreparationWindow = ConfigUIHelpers.FloatSlider(
                    Loc.T(LocalizedStrings.HealerShared.TankBusterWindow, "Tank Buster Window (sec)"),
                    config.Healing.TankBusterPreparationWindow, 1f, 6f, "%.1f",
                    "Seconds before a predicted tank buster to pre-arm single-target mitigation.",
                    save,
                    v => config.Healing.TankBusterPreparationWindow = v);
            }

            ConfigUIHelpers.EndIndent();
        }
    }

    private void DrawBurstWindow()
    {
        if (ConfigUIHelpers.SectionHeader(Loc.T(LocalizedStrings.HealerShared.BurstSection, "Burst Window"), "Healer"))
        {
            ConfigUIHelpers.BeginIndent();

            ConfigUIHelpers.Toggle(
                Loc.T(LocalizedStrings.HealerShared.EnableBurstPooling, "Enable Burst Pooling"),
                () => config.HealerShared.EnableBurstPooling,
                v => config.HealerShared.EnableBurstPooling = v,
                Loc.T(LocalizedStrings.HealerShared.EnableBurstPoolingDesc,
                    "Pool healer damage cooldowns for raid buff burst windows. Holds Chain Stratagem, Phlegma, Psyche, Presence of Mind, and Afflatus Misery until the burst window opens."),
                save);

            ConfigUIHelpers.EndIndent();
        }
    }
}
