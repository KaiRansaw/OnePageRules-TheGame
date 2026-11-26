using FDG;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FdgRaylib.Game;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>The side trays' sorting (Kai, 2026-11-26, mockup A): which units sit in Ambush / Scout / Reserves / Destroyed.</summary>
[TestFixture]
public class OffTableTests
{
    private GameDataStore _store = null!;
    private readonly PlayerID _player = new(Guid.NewGuid());

    [SetUp]
    public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

    private UnitData Unit(string name, int models, Position at, string? rule = null)
    {
        var bindings = Enumerable.Range(0, models)
            .Select(i => _store.GetDataBinding<ModelData>(_store.Create(new ModelData(0.5f, new List<Weapon>(),
                at.x == 0 && at.z == 0 ? at : new Position(at.x + i, at.z), _store))))
            .ToList();
        var unit = _store.GetDataBinding<UnitData>(_store.Create(new UnitData(_player, name, quality: 4, defense: 4, modelBindings: bindings))).GetValue();
        if (rule == "Ambush") unit.AttachRuleDefinition(new ResolvedRule("Ambush", CoreRuleCatalog.Ambush));
        if (rule == "Scout") unit.AttachRuleDefinition(new ResolvedRule("Scout", CoreRuleCatalog.Scout));
        return unit;
    }

    private static readonly Position Off = new(0f, 0f);

    [Test]
    public void OnTheTable_NotOnATray()
    {
        UnitData u = Unit("Warriors", 5, new Position(10f, 10f));
        Assert.That(OffTable.Sort(new[] { u }, 1), Is.Empty);
    }

    [Test]
    public void AnAmbushUnitInReserve_IsInAmbush_AnotherReserveIsInReserves()
    {
        UnitData amb = Unit("Raiders", 5, Off, "Ambush");
        ReserveRules.PlaceInReserve(amb);
        UnitData other = Unit("Grunts", 5, Off);
        ReserveRules.PlaceInReserve(other);
        UnitData respawn = Unit("Lurkers", 3, Off, "Ambush");
        ReserveRules.PlaceInReserve(respawn);
        respawn.Tokens.AddToken(new Token(TokenType.Respawning, 1, new TokenClearTrigger.ManualOnly()));
        var sorted = OffTable.Sort(new[] { amb, other, respawn }, 1);
        Assert.That(sorted.Single(e => e.Unit == amb).Bay, Is.EqualTo(OffTable.Bay.Ambush));
        Assert.That(sorted.Single(e => e.Unit == other).Bay, Is.EqualTo(OffTable.Bay.Reserves));
        Assert.That(sorted.Single(e => e.Unit == respawn).Bay, Is.EqualTo(OffTable.Bay.Reserves), "a respawning unit isn't arriving by Ambush");
    }

    [Test]
    public void InDeployment_AScoutWaits_TheRestAreStillToDeploy()
    {
        UnitData scout = Unit("Rangers", 5, Off, "Scout");
        UnitData plain = Unit("Grunts", 5, Off);
        var sorted = OffTable.Sort(new[] { scout, plain }, null);
        Assert.That(sorted.Select(e => (e.Unit.Name, e.Bay)), Is.EqualTo(new[] { ("Rangers", OffTable.Bay.Scout) }));
        Assert.That(OffTable.Sort(new[] { plain }, 2).Single().Bay, Is.EqualTo(OffTable.Bay.Reserves), "after deployment, off the table = held");
    }

    [Test]
    public void DeadModels_GoToDestroyed_TheLivingStayWhereTheyAre()
    {
        UnitData u = Unit("Warriors", 5, new Position(10f, 10f));
        u.Models[0].DealWounds(1);
        u.Models[1].DealWounds(1);
        var sorted = OffTable.Sort(new[] { u }, 2);
        Assert.That(sorted, Has.Count.EqualTo(1));
        Assert.That(sorted[0].Bay, Is.EqualTo(OffTable.Bay.Destroyed));
        Assert.That(sorted[0].Models, Has.Count.EqualTo(2));
    }

    [Test]
    public void AnEmbarkedUnit_IsInItsTransport_NotOnATray()
    {
        UnitData u = Unit("Cargo", 5, Off);
        u.Tokens.AddToken(new Token(TokenType.EmbarkedIn, 1, new TokenClearTrigger.ManualOnly()));
        Assert.That(OffTable.Sort(new[] { u }, 2), Is.Empty);
    }
}
