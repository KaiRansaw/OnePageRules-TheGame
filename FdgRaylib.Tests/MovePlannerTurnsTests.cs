using FDG;
using FDG.Data;
using FDG.Players;
using FDG.Stages;
using FDG.StageResolution.Requests;
using FdgRaylib.Game;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// Kai, 2026-11-18: turn points (round a building) and laying the models out at the end, in one move. Each model goes
/// through its own place at each turn, then to where it was dragged; its distance is measured along that route.
/// </summary>
[TestFixture]
public class MovePlannerTurnsTests
{
    private GameDataStore _store = null!;
    private PlayerID _me;
    private DataBinding<UnitData> _unit = null!;

    [SetUp]
    public void SetUp()
    {
        _store = GameDataStore.GameDataStoreBuilder.GetDefault();
        _me = new PlayerID(Guid.NewGuid());
        var models = new List<DataBinding<ModelData>>();
        foreach (float z in new[] { 10f, 11.2f })
            models.Add(_store.GetDataBinding<ModelData>(_store.Create(new ModelData(0.5f, new List<Weapon>(), new Position(10f, z), _store))));
        _unit = _store.GetDataBinding<UnitData>(_store.Create(new UnitData(_me, "Squad", quality: 4, defense: 4, modelBindings: models)));
        _store.Create(new ArmyData(_me, new List<DataBinding<UnitData>> { _unit }));
    }

    private DefineMovementPathRequest Request() => new(_me, "Move", _unit,
        maxAdvanceDistance: 6f, maxRushDistance: 12f, maxDistanceInches: 12f, mainActivationMove: true);

    [Test]
    public void ADraggedModel_GoesThroughTheTurns_ThenToItsEnd()
    {
        var models = _unit.GetValue().Models;
        var a = models[0];
        // One turn: the unit's middle 4" along x. Then model A is dragged 3" further in z (past model B).
        var turns = new List<(float X, float Z)> { (14f, 10.6f) };
        var ends = new Dictionary<ModelID, (float X, float Z)> { [a.ID] = (14f, 13f) };
        MovePlanner.Plan plan = MovePlanner.Arranged(Request(), new TableState(_store), ends, turns);

        var path = plan.Moves.First(m => m.Model.GetValue().ID.Equals(a.ID)).Positions;
        Assert.That(path, Has.Count.EqualTo(2), "its place at the turn, then its own end");
        Assert.That(path[0].x, Is.EqualTo(14f).Within(0.01f));
        Assert.That(path[0].z, Is.EqualTo(10f).Within(0.01f));
        Assert.That(path[1].z, Is.EqualTo(13f).Within(0.01f));
        Assert.That(plan.Distance, Is.EqualTo(7f).Within(0.01f), "4\" to the turn + 3\" after it");
        Assert.That(plan.Valid, Is.True, plan.Problem);
    }

    [Test]
    public void TheDragReach_CountsWhatTheTurnsUsed()
    {
        var a = _unit.GetValue().Models[0];
        var turns = new List<(float X, float Z)> { (18f, 10.6f) };   // 8" used getting there
        var start = new Dictionary<ModelID, (float X, float Z)> { [a.ID] = (18f, 10f) };
        var dragged = MovePlanner.Dragged(Request(), start, new[] { a.ID }, 10f, 0f, cap: null, turns: turns);
        Assert.That(dragged[a.ID].X, Is.EqualTo(22f).Within(0.05f), "12\" in all: 8\" to the turn leaves 4\"");
    }
}
