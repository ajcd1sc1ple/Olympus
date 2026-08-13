using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Olympus.Data;
using Olympus.Rotation.AstraeaCore.Abilities;
using Olympus.Rotation.AstraeaCore.Modules.Healing;
using Olympus.Tests.Mocks;
using Olympus.Tests.Rotation.Common.Scheduling;
using Olympus.Timeline;
using Olympus.Timeline.Models;
using Xunit;

namespace Olympus.Tests.Rotation.AstraeaCore.Modules.Healing;

public class TankBusterShieldPrepTests
{
    private static Mock<ITimelineService> TankBusterTimeline(float secondsUntil = 2f)
    {
        var prediction = new MechanicPrediction(secondsUntil, TimelineEntryType.TankBuster, "TestTankBuster", 1f);
        var m = new Mock<ITimelineService>();
        m.Setup(s => s.IsActive).Returns(true);
        m.Setup(s => s.Confidence).Returns(1f);
        m.Setup(s => s.NextTankBuster).Returns((MechanicPrediction?)prediction);
        return m;
    }

    [Fact]
    public void Exaltation_TankBusterImminentFullHp_Pushes()
    {
        var config = AstraeaTestContext.CreateDefaultAstrologianConfiguration();
        config.Astrologian.EnableExaltation = true;
        config.Astrologian.ExaltationThreshold = 0.50f;
        config.Timeline.EnableTimelinePredictions = true;
        config.Timeline.TimelineConfidenceThreshold = 0.8f;

        var tank = MockBuilders.CreateMockBattleChara(entityId: 10u, currentHp: 50000, maxHp: 50000);
        tank.Setup(x => x.StatusList).Returns((Dalamud.Game.ClientState.Statuses.StatusList?)null!);
        var partyHelper = new TestableAstraeaPartyHelper(new List<IBattleChara> { tank.Object }, config);

        var actionService = MockBuilders.CreateMockActionService(canExecuteGcd: true, canExecuteOgcd: false);
        actionService.Setup(x => x.IsActionReady(ASTActions.Exaltation.ActionId)).Returns(true);

        var context = AstraeaTestContext.Create(
            config: config,
            partyHelper: partyHelper,
            actionService: actionService,
            timelineService: TankBusterTimeline(),
            level: 100);

        var scheduler = SchedulerFactory.CreateForTest(actionService);
        new ExaltationHandler().CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectOgcdQueue(),
            c => c.Behavior == AstraeaAbilities.Exaltation && c.TargetId == tank.Object.GameObjectId);
    }

    [Fact]
    public void CelestialIntersection_TankBusterImminentFullHp_Pushes()
    {
        var config = AstraeaTestContext.CreateDefaultAstrologianConfiguration();
        config.Astrologian.EnableCelestialIntersection = true;
        config.Astrologian.CelestialIntersectionThreshold = 0.50f;
        config.Timeline.EnableTimelinePredictions = true;
        config.Timeline.TimelineConfidenceThreshold = 0.8f;

        var tank = MockBuilders.CreateMockBattleChara(entityId: 10u, currentHp: 50000, maxHp: 50000);
        var partyHelper = new TestableAstraeaPartyHelper(new List<IBattleChara> { tank.Object }, config);

        var actionService = MockBuilders.CreateMockActionService(canExecuteGcd: true, canExecuteOgcd: false);
        actionService.Setup(x => x.IsActionReady(ASTActions.CelestialIntersection.ActionId)).Returns(true);

        var context = AstraeaTestContext.Create(
            config: config,
            partyHelper: partyHelper,
            actionService: actionService,
            timelineService: TankBusterTimeline(),
            level: 100);

        var scheduler = SchedulerFactory.CreateForTest(actionService);
        new CelestialIntersectionHandler().CollectCandidates(context, scheduler, isMoving: false);

        Assert.Contains(scheduler.InspectOgcdQueue(),
            c => c.Behavior == AstraeaAbilities.CelestialIntersection && c.TargetId == tank.Object.GameObjectId);
    }
}
