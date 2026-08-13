using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Olympus.Data;
using Olympus.Models.Action;
using Olympus.Rotation.AsclepiusCore.Abilities;
using Olympus.Rotation.AsclepiusCore.Modules.Healing;
using Olympus.Services.Action;
using Olympus.Services.Prediction;
using Olympus.Tests.Mocks;
using Olympus.Tests.Rotation.Common.Scheduling;
using Olympus.Timeline;
using Olympus.Timeline.Models;
using Xunit;

namespace Olympus.Tests.Rotation.AsclepiusCore.Modules.Healing;

public class TankBusterShieldPrepTests
{
    private static Mock<ITimelineService> TankBusterTimeline(float secondsUntil = 2f)
    {
        var prediction = new MechanicPrediction(secondsUntil, TimelineEntryType.TankBuster, "TestTankBuster", 1f);
        var m = new Mock<ITimelineService>();
        m.Setup(s => s.IsActive).Returns(true);
        m.Setup(s => s.Confidence).Returns(1f);
        m.Setup(s => s.NextTankBuster).Returns((MechanicPrediction?)prediction);
        m.Setup(s => s.NextTankBusterForGcdHealPrep).Returns((MechanicPrediction?)prediction);
        return m;
    }

    [Fact]
    public void Krasis_TankBusterImminentFullHp_PushesAtTimelineMitPriority()
    {
        var config = AsclepiusTestContext.CreateDefaultSageConfiguration();
        config.Sage.EnableKrasis = true;
        config.Sage.KrasisThreshold = 0.50f;
        config.Timeline.EnableTimelinePredictions = true;
        config.Timeline.TimelineConfidenceThreshold = 0.8f;

        var tank = MockBuilders.CreateMockBattleChara(entityId: 10u, currentHp: 50000, maxHp: 50000);
        tank.Setup(x => x.StatusList).Returns((Dalamud.Game.ClientState.Statuses.StatusList?)null!);

        var partyHelper = MockBuilders.CreateMockPartyHelper();
        partyHelper.Setup(p => p.FindTankInParty(It.IsAny<Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter>()))
            .Returns(tank.Object);
        partyHelper.Setup(p => p.FindLowestHpPartyMember(It.IsAny<Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter>(), It.IsAny<int>()))
            .Returns(tank.Object);

        var actionService = MockBuilders.CreateMockActionService(canExecuteGcd: true, canExecuteOgcd: false);
        actionService.Setup(x => x.IsActionReady(SGEActions.Krasis.ActionId)).Returns(true);

        var context = AsclepiusTestContext.Create(
            config: config,
            partyHelper: partyHelper,
            actionService: actionService,
            timelineService: TankBusterTimeline(),
            level: 100);

        var scheduler = SchedulerFactory.CreateForTest(actionService);
        new KrasisHandler().CollectCandidates(context, scheduler, isMoving: false);

        var candidate = Assert.Single(scheduler.InspectOgcdQueue(), c => c.Behavior == AsclepiusAbilities.Krasis);
        // TimelineMitigation (8) + TimelineTankBusterOffset (1)
        Assert.Equal(9, candidate.Priority);
        Assert.Equal(tank.Object.GameObjectId, candidate.TargetId);
    }

    [Fact]
    public void ShieldHealing_TankBusterImminentFullHp_DirectDispatchesEukrasia()
    {
        var config = AsclepiusTestContext.CreateDefaultSageConfiguration();
        config.Sage.EnableEukrasianDiagnosis = true;
        config.Sage.EukrasianDiagnosisThreshold = 0.50f;
        config.Sage.EnableEukrasianPrognosis = false;
        config.Timeline.EnableTimelinePredictions = true;
        config.Timeline.TimelineConfidenceThreshold = 0.8f;

        var tank = MockBuilders.CreateMockBattleChara(entityId: 10u, currentHp: 50000, maxHp: 50000);
        tank.Setup(x => x.StatusList).Returns((Dalamud.Game.ClientState.Statuses.StatusList?)null!);

        var partyHelper = MockBuilders.CreateMockPartyHelper();
        partyHelper.Setup(p => p.CalculatePartyHealthMetrics(It.IsAny<Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter>()))
            .Returns((avgHpPercent: 1.0f, lowestHpPercent: 1.0f, injuredCount: 0));
        partyHelper.Setup(p => p.FindTankInParty(It.IsAny<Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter>()))
            .Returns(tank.Object);
        partyHelper.Setup(p => p.FindLowestHpPartyMember(It.IsAny<Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter>(), It.IsAny<int>()))
            .Returns(tank.Object);

        var actionService = MockBuilders.CreateMockActionService(canExecuteGcd: true);
        actionService.Setup(x => x.ExecuteOgcd(
                It.Is<ActionDefinition>(a => a.ActionId == SGEActions.Eukrasia.ActionId),
                It.IsAny<ulong>()))
            .Returns(true);

        var context = AsclepiusTestContext.Create(
            config: config,
            partyHelper: partyHelper,
            actionService: actionService,
            timelineService: TankBusterTimeline(),
            level: 100,
            hasEukrasia: false);

        var scheduler = SchedulerFactory.CreateForTest(actionService);
        new ShieldHealingHandler().CollectCandidates(context, scheduler, isMoving: false);

        actionService.Verify(x => x.ExecuteOgcd(
            It.Is<ActionDefinition>(a => a.ActionId == SGEActions.Eukrasia.ActionId),
            It.IsAny<ulong>()), Times.Once);
    }

    [Fact]
    public void ShieldHealing_TankBusterCastHintOnly_WhileMoving_StillArmsEukrasia()
    {
        // BossMod often only exposes TB via cast-hint; GCD prep must use it.
        // Eukrasian shields are instant — movement must not block prep.
        var config = AsclepiusTestContext.CreateDefaultSageConfiguration();
        config.Sage.EnableEukrasianDiagnosis = true;
        config.Sage.EukrasianDiagnosisThreshold = 0.50f;
        config.Sage.EnableEukrasianPrognosis = false;
        config.Timeline.EnableTimelinePredictions = true;
        config.Timeline.TimelineConfidenceThreshold = 0.8f;

        var castHintOnly = new MechanicPrediction(2f, TimelineEntryType.TankBuster, "BossMod tankbuster (cast)", 0.95f);
        var timeline = new Mock<ITimelineService>();
        timeline.Setup(s => s.IsActive).Returns(true);
        timeline.Setup(s => s.Confidence).Returns(1f);
        timeline.Setup(s => s.NextTankBuster).Returns((MechanicPrediction?)castHintOnly);
        timeline.Setup(s => s.NextTankBusterForGcdHealPrep).Returns((MechanicPrediction?)castHintOnly);

        var tank = MockBuilders.CreateMockBattleChara(entityId: 10u, currentHp: 50000, maxHp: 50000);
        tank.Setup(x => x.StatusList).Returns((Dalamud.Game.ClientState.Statuses.StatusList?)null!);

        var partyHelper = MockBuilders.CreateMockPartyHelper();
        partyHelper.Setup(p => p.CalculatePartyHealthMetrics(It.IsAny<Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter>()))
            .Returns((avgHpPercent: 1.0f, lowestHpPercent: 1.0f, injuredCount: 0));
        partyHelper.Setup(p => p.FindTankInParty(It.IsAny<Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter>()))
            .Returns(tank.Object);
        partyHelper.Setup(p => p.FindLowestHpPartyMember(It.IsAny<Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter>(), It.IsAny<int>()))
            .Returns(tank.Object);

        var actionService = MockBuilders.CreateMockActionService(canExecuteGcd: true);
        actionService.Setup(x => x.ExecuteOgcd(
                It.Is<ActionDefinition>(a => a.ActionId == SGEActions.Eukrasia.ActionId),
                It.IsAny<ulong>()))
            .Returns(true);

        var context = AsclepiusTestContext.Create(
            config: config,
            partyHelper: partyHelper,
            actionService: actionService,
            timelineService: timeline,
            level: 100,
            hasEukrasia: false);

        var scheduler = SchedulerFactory.CreateForTest(actionService);
        new ShieldHealingHandler().CollectCandidates(context, scheduler, isMoving: true);

        actionService.Verify(x => x.ExecuteOgcd(
            It.Is<ActionDefinition>(a => a.ActionId == SGEActions.Eukrasia.ActionId),
            It.IsAny<ulong>()), Times.Once);
    }
}
