using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Olympus.Data;
using Olympus.Rotation.AthenaCore.Abilities;
using Olympus.Rotation.AthenaCore.Modules.Healing;
using Olympus.Tests.Mocks;
using Olympus.Tests.Rotation.Common.Scheduling;
using Olympus.Timeline;
using Olympus.Timeline.Models;
using Xunit;

namespace Olympus.Tests.Rotation.AthenaCore.Modules.Healing;

/// <summary>
/// Tank-buster prep: Adlo must land on a full-HP tank so Galvanize is up before impact.
/// </summary>
public class SingleTargetHealHandlerTankBusterTests
{
    private readonly SingleTargetHealHandler _handler = new();

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
    public void CollectCandidates_TankBusterImminentFullHp_PushesAdloquium()
    {
        var config = AthenaTestContext.CreateDefaultScholarConfiguration();
        config.Scholar.EnableAdloquium = true;
        config.Scholar.AdloquiumThreshold = 0.65f;
        config.Timeline.EnableTimelinePredictions = true;
        config.Timeline.TimelineConfidenceThreshold = 0.8f;

        // Full HP — below-threshold path would skip; TB path must still push Adlo.
        var tank = MockBuilders.CreateMockBattleChara(entityId: 10u, currentHp: 50000, maxHp: 50000);
        tank.Setup(x => x.StatusList).Returns((Dalamud.Game.ClientState.Statuses.StatusList?)null!);
        var partyHelper = new TestableAthenaPartyHelper(new List<IBattleChara> { tank.Object }, config);

        var actionService = MockBuilders.CreateMockActionService(canExecuteGcd: true, canExecuteOgcd: false);
        var timeline = TankBusterTimeline(secondsUntil: 2f);

        var context = AthenaTestContext.Create(
            config: config,
            partyHelper: partyHelper,
            actionService: actionService,
            timelineService: timeline,
            level: 100,
            canExecuteGcd: true);

        var scheduler = SchedulerFactory.CreateForTest(actionService);

        _handler.CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectGcdQueue(),
            c => c.Behavior == AthenaAbilities.Adloquium && c.TargetId == tank.Object.GameObjectId);
    }

    [Fact]
    public void CollectCandidates_FullHpNoTankBuster_DoesNotPushAdloquium()
    {
        var config = AthenaTestContext.CreateDefaultScholarConfiguration();
        config.Scholar.EnableAdloquium = true;
        config.Scholar.AdloquiumThreshold = 0.65f;

        var member = MockBuilders.CreateMockBattleChara(entityId: 10u, currentHp: 50000, maxHp: 50000);
        member.Setup(x => x.StatusList).Returns((Dalamud.Game.ClientState.Statuses.StatusList?)null!);
        var partyHelper = new TestableAthenaPartyHelper(new List<IBattleChara> { member.Object }, config);

        var actionService = MockBuilders.CreateMockActionService(canExecuteGcd: true, canExecuteOgcd: false);

        var context = AthenaTestContext.Create(
            config: config,
            partyHelper: partyHelper,
            actionService: actionService,
            level: 100,
            canExecuteGcd: true);

        var scheduler = SchedulerFactory.CreateForTest(actionService);

        _handler.CollectCandidates(context, scheduler, isMoving: false);

        Assert.DoesNotContain(scheduler.InspectGcdQueue(),
            c => c.Behavior == AthenaAbilities.Adloquium);
    }
}
