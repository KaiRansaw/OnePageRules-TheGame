using FDG;
using FDG.Data;
using FDG.StageResolution.Requests;
using FdgRaylib.Game;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>Hold-and-drag to turn a unit while deploying or moving (Kai, 2026-11-12): round bases turn, others keep their footprint.</summary>
[TestFixture]
public class FacingAimTests
{
    private readonly IReadWriteableGameDataStore _store = GameDataStore.GameDataStoreBuilder.GetDefault();

    private DataBinding<ModelData> Model(IBaseShape shape, float x, float z) =>
        _store.GetDataBinding<ModelData>(_store.Create(new ModelData(shape, new List<Weapon>(), new Position(x, z), _store)));

    [Test]
    public void From_PointsFromTheMiddleToThePointer_AndIgnoresTinyDrags()
    {
        Float2 f = FacingAim.From(10, 10, 13, 14)!.Value;
        Assert.That(f.X, Is.EqualTo(0.6f).Within(1e-4));
        Assert.That(f.Y, Is.EqualTo(0.8f).Within(1e-4));
        Assert.That(FacingAim.From(10, 10, 10.2f, 10.2f), Is.Null);
    }

    [Test]
    public void Placement_TurnsRoundBasesOnly()
    {
        var round = Model(new CircleBase(0.5f), 5, 5);
        var tank = Model(new RectangleBase(3, 5), 10, 5);
        var plan = new List<PlacedObjectEntry<ModelData>> { new(round, new Position(5, 5), new Float2(0, 1)), new(tank, new Position(10, 5), new Float2(0, 1)) };
        var turned = FacingAim.Turn(plan, new Float2(1, 0));
        Assert.That(turned[0].Facing, Is.EqualTo(new Float2(1, 0)));
        Assert.That(turned[1].Facing, Is.EqualTo(new Float2(0, 1)), "a rectangle base keeps the facing its footprint was checked with");
        Assert.That(FacingAim.Shown(turned[0]), Is.EqualTo(new Float2(1, 0)));
    }

    [Test]
    public void Move_EndsFacingTheChosenWay_EarlierLegsFaceTheWayWalked()
    {
        var round = Model(new CircleBase(0.5f), 0, 0);
        var moves = new List<ModelMoveEntry> { new(round, new List<Position> { new(0, 4), new(4, 4) }) };
        Assert.That(FacingAim.Shown(moves[0]), Is.EqualTo(new Float2(1, 0)), "no choice yet: the way it walked the last leg");
        var turned = FacingAim.Turn(moves, new Float2(0, -1));
        Assert.That(turned[0].Facings, Is.EqualTo(new List<Float2> { new(0, 1), new(0, -1) }));
        Assert.That(FacingAim.Shown(turned[0]), Is.EqualTo(new Float2(0, -1)));
    }
}
