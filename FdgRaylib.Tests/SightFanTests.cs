using System;
using System.Collections.Generic;
using System.Linq;
using FDG;
using FDG.Data;
using FDG.Stages;
using FdgRaylib.Game;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// The "what can this unit see" picture (Godot, F): bands per weapon range, shadows behind sight blockers, and the
/// Volumetric method seeing over low walls - all from the engine's own sight test.
/// Table: a shooter at (10,20) with a 12" and a 24" weapon; a wall at x 16..17, z 16..24.
/// </summary>
[TestFixture]
public class SightFanTests
{
    private GameDataStore _store = null!;
    private PlayerID _me;

    [SetUp]
    public void SetUp()
    {
        _store = GameDataStore.GameDataStoreBuilder.GetDefault();
        _me = new PlayerID(Guid.NewGuid());
        _store.Create(new TeamData(0, new List<PlayerID> { _me }));
    }

    [TearDown]
    public void Restore() => GameWideConstants.ConfigureTable(EGameSystem.GrimdarkFuture);

    private IUnit Shooter(float baseMm)
    {
        var weapons = new List<Weapon> { new Weapon("Pistol", 12f, 1, 0), new Weapon("Rifle", 24f, 1, 0) };
        var model = new ModelData(baseMm / 25.4f / 2f, weapons, new Position(10f, 20f), _store);
        var bindings = new List<DataBinding<ModelData>> { _store.GetDataBinding<ModelData>(_store.Create(model)) };
        var unit = new UnitData(_me, "Shooters", quality: 4, defense: 4, modelBindings: bindings);
        DataBinding<UnitData> b = _store.GetDataBinding<UnitData>(_store.Create(unit));
        _store.Create(new ArmyData(_me, new List<DataBinding<UnitData>> { b }));
        return b.GetValue();
    }

    private void Wall(float height)
        => _store.Create(new TerrainData(ETerrainType.Blocking, new RectangularZone(16, 17, 16, 24), height));

    private SightFan.Result Fan(IUnit unit) => SightFan.Compute(new TableState(_store), unit, seeThroughFriendlyUnits: false);

    private static SightFan.Cell At(SightFan.Result r, float x, float z)
        => r.Cells.Single(c => MathF.Abs(c.X - x) < 0.01f && MathF.Abs(c.Z - z) < 0.01f);

    [Test]
    public void Bands_FollowTheRanges_ShortestFirst()
    {
        SightFan.Result r = Fan(Shooter(32));
        Assert.That(r.Ranges, Is.EqualTo(new[] { 12f, 24f }));
        Assert.That(r.RangeNames, Is.EqualTo(new[] { "Pistol", "Rifle" }));
        Assert.That(At(r, 10.5f, 25.5f).Band, Is.EqualTo(0), "5\" away: pistol range");
        Assert.That(At(r, 10.5f, 39.5f).Band, Is.EqualTo(1), "19\" away: rifle only");
        Assert.That(r.Cells.Any(c => MathF.Abs(c.Z - 50.5f) < 0.01f && MathF.Abs(c.X - 10.5f) < 0.01f), Is.False,
            "30\" away is out of every range - no cell");
    }

    [Test]
    public void AWall_CastsAShadow_AndCellsInsideItAreLeftOut()
    {
        Wall(3f);
        SightFan.Result r = Fan(Shooter(32));
        Assert.That(At(r, 20.5f, 20.5f).Band, Is.EqualTo(-1), "behind the wall: in reach, hidden");
        Assert.That(At(r, 14.5f, 20.5f).Band, Is.EqualTo(0), "in front of it: seen");
        Assert.That(r.Cells.Any(c => c.X > 16 && c.X < 17 && c.Z > 16 && c.Z < 24), Is.False, "nobody stands in the wall");
    }

    [Test]
    public void Volumetric_ABigModelSeesOverALowWall()
    {
        Wall(1f);
        IUnit big = Shooter(100);
        Assert.That(At(Fan(big), 28.5f, 20.5f).Band, Is.EqualTo(-1), "classic: every wall blocks");
        VolumetricSight.On = true;
        SightFan.Result r = Fan(big);
        Assert.That(r.Volumetric, Is.True);
        Assert.That(At(r, 28.5f, 20.5f).Band, Is.EqualTo(1), "a 4\" model sees over a 1\" wall");
    }
}
