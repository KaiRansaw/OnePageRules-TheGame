using System;
using System.Collections.Generic;
using System.Linq;
using FDG;
using FDG.Data;
using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// #414 - naming the damaged model. OPR assigns wounds to one model at a time (Tough(X): "You must
/// continue to put wounds on the Tough model with most wounds in the unit until it is killed"), and the
/// engine enforces it, so a unit's remaining staying power lives in ONE part-wounded body. The unit health
/// bar is an aggregate that cannot point at it; this is what can.
///
/// <para>Real <c>UnitData</c>/<c>ModelData</c> throughout, like <c>HealthBarRendererTests</c>: the readout
/// reads <c>TotalWounds</c> and the new <c>IModel.RemainingWounds</c> off the interface, and a hand-rolled
/// fake would not prove it reads the right fields or that the floor on an overkilled model works.</para>
/// </summary>
[TestFixture]
public class WoundedModelReadoutTests
{
    // ── IsPartWounded: the one state worth drawing ──────────────────────────────────────────────────

    [Test]
    public void IsPartWounded_OnlyForAnAliveModelThatHasTakenWounds()
    {
        IUnit unit = Squad(3f, 3f, 3f);
        IModel whole = unit.Models[0], hurt = unit.Models[1], dead = unit.Models[2];
        hurt.DealWounds(2f);
        dead.DealWounds(3f);

        Assert.That(WoundedModelReadout.IsPartWounded(whole), Is.False, "a full model has nothing to report");
        Assert.That(WoundedModelReadout.IsPartWounded(hurt), Is.True, "THE case: alive, down to 1 of 3");
        Assert.That(WoundedModelReadout.IsPartWounded(dead), Is.False,
            "a casualty's wounds are told by its absence, and it is not drawn on the table at all");
    }

    [Test]
    public void IsPartWounded_IsNeverTrueForAOneWoundModel()
    {
        // This is what keeps the table quiet, and it is worth pinning rather than leaving to arithmetic: a
        // plain trooper has no state between whole and dead, so a 10-man squad of rifles never shows a
        // single counter however much it is shot at. Only Tough models can be caught in between.
        IUnit unit = Squad(1f, 1f);
        Assert.That(WoundedModelReadout.IsPartWounded(unit.Models[0]), Is.False, "whole");

        unit.Models[1].DealWounds(1f);
        Assert.That(WoundedModelReadout.IsPartWounded(unit.Models[1]), Is.False, "dead, not part-wounded");
    }

    [Test]
    public void IsPartWounded_IgnoresFloatNoise()
    {
        // Wound counts are floats and arrive as hit x save x regeneration expected values (#199), so a
        // model can sit a hair off full. A sliver must not light up a counter on a healthy model.
        IUnit unit = Squad(3f);
        unit.Models[0].DealWounds(0.0001f);

        Assert.That(WoundedModelReadout.IsPartWounded(unit.Models[0]), Is.False,
            "below the shared epsilon - and it must agree with the health bar, which hides at full");
    }

    [Test]
    public void RemainingWounds_IsFlooredOnAnOverkilledModel()
    {
        // The engine member this readout is built on. Overkill is routine - a big volley or a Deadly clump
        // drives WoundsDealt past TotalWounds - and a negative would both read as a bug on screen and
        // subtract from a sibling in any sum.
        IUnit unit = Squad(3f);
        unit.Models[0].DealWounds(10f);

        Assert.That(unit.Models[0].RemainingWounds, Is.EqualTo(0f));
        Assert.That(WoundFormat.Format(unit.Models[0].RemainingWounds), Is.EqualTo("0"), "never \"-7\" or \"-0\"");
    }

    // ── Ordering ────────────────────────────────────────────────────────────────────────────────────

    [Test]
    public void Wounded_OrdersTheClosestToDeathFirst()
    {
        // Not cosmetic. By the Tough rule the next hit lands on the model with the most wounds already, so
        // the nearest-dead model is both the most urgent and the one the next volley will finish - it is
        // the one to keep when the card line truncates.
        IUnit unit = Squad(6f, 6f, 6f);
        unit.Models[0].DealWounds(1f);   // 5 left
        unit.Models[1].DealWounds(4f);   // 2 left
        unit.Models[2].DealWounds(2f);   // 4 left

        Assert.That(WoundedModelReadout.Wounded(unit).Select(m => m.RemainingWounds),
            Is.EqualTo(new[] { 2f, 4f, 5f }));
    }

    [Test]
    public void Wounded_SkipsWholeAndDeadModels()
    {
        IUnit unit = Squad(3f, 3f, 3f, 3f);
        unit.Models[1].DealWounds(3f);   // dead
        unit.Models[2].DealWounds(1f);   // the only one

        Assert.That(WoundedModelReadout.Wounded(unit).Count, Is.EqualTo(1));
        Assert.That(WoundedModelReadout.Wounded(unit)[0], Is.SameAs(unit.Models[2]));
    }

    // ── The card line ───────────────────────────────────────────────────────────────────────────────

    [Test]
    public void CardLine_IsNullWhenNothingIsPartWounded()
    {
        // The common case by a wide margin, and the line has to cost nothing when there is nothing to say -
        // the unit card is already dense.
        Assert.That(WoundedModelReadout.CardLine(Squad(3f, 3f)), Is.Null, "untouched unit");

        IUnit mauled = Squad(3f, 3f);
        mauled.Models[0].DealWounds(3f);
        Assert.That(WoundedModelReadout.CardLine(mauled), Is.Null,
            "a clean kill leaves no part-wounded model, so the unit total alone tells the story");
    }

    [Test]
    public void CardLine_NamesTheSingleWoundedModel()
    {
        IUnit unit = Squad(3f, 3f, 3f);
        unit.Models[1].DealWounds(2f);

        Assert.That(WoundedModelReadout.CardLine(unit), Is.EqualTo("Wounded model: 1/3 wounds left"));
    }

    [Test]
    public void CardLine_PluralisesAndListsWhenTheEngineLeavesSeveral()
    {
        // AssignWoundsResults pre-assigns to every already-wounded model before starting a new one, and
        // when the packet runs dry part way through it can leave two mid-fill. That is a documented engine
        // gap, not an invariant, so the line must survive it rather than assume one.
        IUnit unit = Squad(6f, 6f);
        unit.Models[0].DealWounds(1f);   // 5 left
        unit.Models[1].DealWounds(4f);   // 2 left

        Assert.That(WoundedModelReadout.CardLine(unit),
            Is.EqualTo("Wounded models: 2/6, 5/6 wounds left"), "nearest-dead first");
    }

    [Test]
    public void CardLine_TruncatesALongList()
    {
        IUnit unit = Squad(6f, 6f, 6f, 6f, 6f);
        for (int i = 0; i < 5; i++) unit.Models[i].DealWounds(i + 1);   // 5,4,3,2,1 left

        Assert.That(WoundedModelReadout.CardLine(unit),
            Is.EqualTo("Wounded models: 1/6, 2/6, 3/6 (+2 more) wounds left"),
            "a roster of part-wounded models is a rules anomaly, not a readout to scroll");
    }

    [Test]
    public void Counter_GoesThroughWoundFormat_SoFractionsStayReadable()
    {
        // The probabilistic roller deals fractional wounds, and the two naive spellings both mislead:
        // bare interpolation prints "1.6666667/3", F0 truncates to "1/3" - a number the engine is not
        // working with. Hundredths, trailing zeros dropped (#287).
        IUnit unit = Squad(3f);
        unit.Models[0].DealWounds(1f + 1f / 3f);

        Assert.That(WoundedModelReadout.Counter(unit.Models[0]), Is.EqualTo("1.67/3"));

        IUnit whole = Squad(3f);
        whole.Models[0].DealWounds(1f);
        Assert.That(WoundedModelReadout.Counter(whole.Models[0]), Is.EqualTo("2/3"), "no \"2.00/3\"");
    }

    [Test]
    public void CardLine_IsAscii()
    {
        // CLAUDE.md: the ImGui font atlas bakes Basic Latin + Latin-1 only, so a stray dash or ellipsis
        // renders as "?" in game. This line is built by hand, so it is born ASCII or not at all.
        IUnit unit = Squad(6f, 6f, 6f, 6f);
        for (int i = 0; i < 4; i++) unit.Models[i].DealWounds(i + 1);

        string line = WoundedModelReadout.CardLine(unit)!;
        Assert.That(line.All(c => c <= 0x7F), Is.True, $"non-ASCII in \"{line}\"");
    }

    // A unit of models with the given max wounds each, undamaged. Same shape as HealthBarRendererTests.
    private static IUnit Squad(params float[] woundsPerModel)
    {
        var store = GameDataStore.GameDataStoreBuilder.GetDefault();
        var bindings = new List<DataBinding<ModelData>>();
        foreach (float wounds in woundsPerModel)
        {
            var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
                initialPosition: new Position(), gameDataStore: store);
            model.SetMaxWounds((int)wounds);
            bindings.Add(store.GetDataBinding<ModelData>(store.Create(model)));
        }
        return new UnitData(new PlayerID(Guid.NewGuid()), "Squad", quality: 4, defense: 4,
            modelBindings: bindings);
    }
}
