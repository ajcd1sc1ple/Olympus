using System.Collections.Generic;
using System.Numerics;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Party;
using Dalamud.Plugin.Services;
using Moq;
using Olympus.Config;
using Olympus.Services.Targeting;
using Xunit;

namespace Olympus.Tests.Services.Targeting;

/// <summary>
/// Pack adds often lag on <see cref="StatusFlags.InCombat"/> after a pull. Once the
/// player is in combat, Count / Find* / FindBestAoE must all see the same set.
/// </summary>
public sealed class EngagementFilterTests
{
    private static Mock<IBattleNpc> MakeEnemy(ulong id, uint hp, StatusFlags flags, Vector3? pos = null)
    {
        var mock = new Mock<IBattleNpc>();
        mock.Setup(x => x.GameObjectId).Returns(id);
        mock.Setup(x => x.EntityId).Returns((uint)id);
        mock.Setup(x => x.ObjectKind).Returns(ObjectKind.BattleNpc);
        mock.Setup(x => x.IsTargetable).Returns(true);
        mock.Setup(x => x.IsDead).Returns(false);
        mock.Setup(x => x.CurrentDistance).Returns(5);
        mock.Setup(x => x.SubKind).Returns((byte)0);
        mock.Setup(x => x.Position).Returns(pos ?? Vector3.Zero);
        mock.Setup(x => x.HitboxRadius).Returns(0.5f);
        mock.Setup(x => x.StatusFlags).Returns(flags);
        mock.Setup(x => x.CurrentHp).Returns(hp);
        mock.Setup(x => x.StatusList).Returns((Dalamud.Game.ClientState.Statuses.StatusList?)null);
        return mock;
    }

    private static IPlayerCharacter MakePlayer(StatusFlags flags = 0)
    {
        var mock = new Mock<IPlayerCharacter>();
        mock.Setup(x => x.Position).Returns(Vector3.Zero);
        mock.Setup(x => x.HitboxRadius).Returns(0.5f);
        mock.Setup(x => x.GameObjectId).Returns(999ul);
        mock.Setup(x => x.StatusFlags).Returns(flags);
        return mock.Object;
    }

    private static TargetingService BuildService(
        IEnumerable<IBattleNpc> enemies,
        IGameObject? currentTarget = null)
    {
        var objectTableMock = new Mock<IObjectTable>();
        var enemyList = new List<IGameObject>();
        foreach (var e in enemies) enemyList.Add(e);
        objectTableMock
            .Setup(x => x.GetEnumerator())
            .Returns(() => enemyList.GetEnumerator());
        objectTableMock
            .Setup(x => x.SearchById(It.IsAny<ulong>()))
            .Returns((ulong id) => enemyList.Find(e => e.GameObjectId == id));

        var partyListMock = new Mock<IPartyList>();
        partyListMock.Setup(x => x.GetEnumerator()).Returns(new List<IPartyMember>().GetEnumerator());

        var targetManagerMock = new Mock<ITargetManager>();
        targetManagerMock.Setup(x => x.Target).Returns(currentTarget);
        targetManagerMock.Setup(x => x.FocusTarget).Returns((IGameObject?)null);

        var config = new Configuration();
        config.Targeting.TargetCacheTtlMs = 0;
        config.Targeting.PauseWhenNoTarget = false;

        return new TargetingService(
            objectTableMock.Object,
            partyListMock.Object,
            targetManagerMock.Object,
            config,
            new Mock<IGapCloserSafetyService>().Object);
    }

    [Fact]
    public void PlayerInCombat_IncludesPackAdd_WithoutEnemyInCombatFlag()
    {
        // Hard target is engaged; sibling add has not received InCombat yet (common on pull).
        var hardTarget = MakeEnemy(1, hp: 5_000, StatusFlags.InCombat);
        var laggingAdd = MakeEnemy(2, hp: 1_000, flags: 0);

        var svc = BuildService([hardTarget.Object, laggingAdd.Object], currentTarget: hardTarget.Object);
        var player = MakePlayer(StatusFlags.InCombat);

        var lowest = svc.FindEnemy(EnemyTargetingStrategy.LowestHp, 25f, player);
        Assert.NotNull(lowest);
        Assert.Equal(2ul, lowest!.GameObjectId);

        Assert.Equal(2, svc.CountEnemiesInRange(25f, player));

        var (aoeTarget, hitCount) = svc.FindBestAoETarget(5f, 25f, player);
        Assert.NotNull(aoeTarget);
        Assert.Equal(2, hitCount);
    }

    [Fact]
    public void PlayerOutOfCombat_ExcludesUnpulledPack_WithoutHardTarget()
    {
        var adjacent = MakeEnemy(3, hp: 2_000, flags: 0);

        var svc = BuildService([adjacent.Object]);
        var player = MakePlayer(flags: 0);

        Assert.Null(svc.FindEnemy(EnemyTargetingStrategy.LowestHp, 25f, player));
        Assert.Equal(0, svc.CountEnemiesInRange(25f, player));
        Assert.Null(svc.FindBestAoETarget(5f, 25f, player).target);
    }

    [Fact]
    public void PlayerOutOfCombat_HardTargetWithoutInCombat_StillSelectable()
    {
        // Striking dummy / pre-pull hard target: no InCombat on player or enemy.
        var dummy = MakeEnemy(4, hp: 50_000, flags: 0);

        var svc = BuildService([dummy.Object], currentTarget: dummy.Object);
        var player = MakePlayer(flags: 0);

        var target = svc.FindEnemy(EnemyTargetingStrategy.LowestHp, 25f, player);
        Assert.NotNull(target);
        Assert.Equal(4ul, target!.GameObjectId);
        Assert.Equal(1, svc.CountEnemiesInRange(25f, player));
    }

    [Fact]
    public void PlayerOutOfCombat_HardTargetOnly_DoesNotPullAdjacentPack()
    {
        var hardTarget = MakeEnemy(5, hp: 8_000, StatusFlags.InCombat, pos: new Vector3(1f, 0f, 0f));
        var adjacent = MakeEnemy(6, hp: 500, flags: 0, pos: new Vector3(2f, 0f, 0f));

        var svc = BuildService([hardTarget.Object, adjacent.Object], currentTarget: hardTarget.Object);
        var player = MakePlayer(flags: 0);

        // Hard target is selectable via InCombat; adjacent pack member is not.
        Assert.Equal(1, svc.CountEnemiesInRange(25f, player));
        var lowest = svc.FindEnemy(EnemyTargetingStrategy.LowestHp, 25f, player);
        Assert.NotNull(lowest);
        Assert.Equal(5ul, lowest!.GameObjectId);
    }
}
