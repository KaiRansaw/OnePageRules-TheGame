using System;
using System.Collections.Generic;
using System.Linq;
using FDG;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// UI rework batch D (Kai's wish list, 2026-11-07): the in-game Active Unit card, Army Lists portrait cards,
/// the round summary card, Load Game's filters and the Map Maker's grouping and Copy. The drawing is
/// rental; what is pinned here is what each screen SAYS and how it decides - the permanent part.
/// </summary>
[TestFixture]
public class UiReworkBatchDTests
{
    // ── Active Unit: grows to fit, never scrolls for a normal unit ──────────────────────────────────

    [Test]
    public void TheActiveUnitCardIsAsTallAsItsContent()
    {
        int actions = (int)(1080 * 0.30f);
        Assert.That(RaylibRenderer.UnitInfoPanelHeight(420.4f, 1080, actions), Is.EqualTo(421),
            "a unit whose card needs 420px gets 420px - no scrollbar, no wasted space.");
        Assert.That(RaylibRenderer.UnitInfoPanelHeight(600f, 1080, actions), Is.EqualTo(600),
            "a bigger unit grows the card; the old fixed 30% (324px) would have scrolled.");
    }

    [Test]
    public void TheActiveUnitCardOnlyStopsGrowingWhereTheActionsAndTabsWouldNoLongerFit()
    {
        int actions = (int)(1080 * 0.30f);
        int tall = RaylibRenderer.UnitInfoPanelHeight(5000f, 1080, actions);
        Assert.That(tall + actions, Is.LessThanOrEqualTo(1080 - 120),
            "the Actions panel and at least a strip of the tabs must stay on screen.");
        Assert.That(RaylibRenderer.UnitInfoPanelHeight(0f, 1080, actions), Is.GreaterThan(0),
            "with nothing selected the card keeps a small height so the column keeps its shape.");
    }

    // ── Round summary card ──────────────────────────────────────────────────────────────────────────

    [Test]
    public void ARoundSummaryLineSaysObjectivesHeldAndWhatWasLost()
    {
        var side = new RoundSideSummary
        {
            Name = "Kai", ObjectivesHeld = 1, ObjectivesChange = 1,
            UnitsLost = new List<string> { "Assault Grunts", "Battle Brothers" },
        };
        Assert.That(RaylibRenderer.RoundSummaryLine(side),
            Is.EqualTo("1 objective held (+1), lost: Assault Grunts, Battle Brothers"));
    }

    [Test]
    public void ARoundSummaryLineShowsVpOnlyWhereTheGameKeepsThemAndSaysWhenLossesWerentRecorded()
    {
        var cards = new RoundSideSummary { ObjectivesHeld = 2, ObjectivesChange = -1, Vp = 3, VpChange = 2,
            UnitsLost = new List<string>() };
        Assert.That(RaylibRenderer.RoundSummaryLine(cards), Is.EqualTo("2 objectives held (-1), 3 VP (+2), no units lost"));

        var resumed = new RoundSideSummary { ObjectivesHeld = 0, UnitsLost = null };
        Assert.That(RaylibRenderer.RoundSummaryLine(resumed), Does.Contain("not recorded"),
            "a round resumed from a save has no start-of-round snapshot - say so rather than claim none were lost.");
    }

    // ── Army Lists (L): live portrait cards ─────────────────────────────────────────────────────────

    private GameDataStore _store = null!;

    [SetUp]
    public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

    private UnitData MakeUnit(string name, int models = 1)
    {
        var bindings = new List<DataBinding<ModelData>>();
        for (int i = 0; i < models; i++)
        {
            var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
                initialPosition: new Position(i, 0f), gameDataStore: _store);
            bindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
        }
        var unit = new UnitData(new PlayerID(Guid.NewGuid()), name, quality: 4, defense: 5, modelBindings: bindings);
        _store.Create(unit);
        return unit;
    }

    [Test]
    public void ALiveCardIsTaggedWithTheUnitsState()
    {
        UnitData ready = MakeUnit("Warriors");
        UnitData spent = MakeUnit("Gunners");
        UnitData shaken = MakeUnit("Scouts");
        shaken.Tokens.AddToken(new Token(TokenType.Shaken, 1, new TokenClearTrigger.ManualOnly()));
        UnitData reserve = MakeUnit("Ambushers");
        ReserveRules.PlaceInReserve(reserve);

        var progress = new FakeProgress(round: 2, unactivated: new IUnit[] { ready, shaken });

        Assert.That(ArmyListOverlay.CardTag(ready, progress).Tag, Is.Null, "ready to act: no tag.");
        Assert.That(ArmyListOverlay.CardTag(spent, progress).Tag, Is.EqualTo("Activated"));
        Assert.That(ArmyListOverlay.CardTag(shaken, progress).Tag, Is.EqualTo("Shaken"));
        Assert.That(ArmyListOverlay.CardTag(reserve, progress).Tag, Is.EqualTo("In reserve"));
    }

    [Test]
    public void ALiveCardSaysModelsStandingAndTheHoverHasTheStats()
    {
        UnitData unit = MakeUnit("Warriors", models: 3);
        unit.PointCost = 90;
        Assert.That(ArmyListOverlay.CardFacts(unit), Is.EqualTo("3/3 models - 90pts"));

        string hover = ArmyListOverlay.LiveTooltip(unit);
        Assert.That(hover, Does.StartWith("Warriors [3] - 90 pts"));
        Assert.That(hover, Does.Contain("Quality 4+   Defense 5+"));
        Assert.That(hover, Does.Contain("Click for the full unit."));
    }

    // ── Load Game filters ───────────────────────────────────────────────────────────────────────────

    [Test]
    public void LoadGameFiltersSplitTheTwoFamiliesAndAllShowsEverything()
    {
        foreach (EGameSystem gf in new[] { EGameSystem.GrimdarkFuture, EGameSystem.GFFirefight, EGameSystem.GFStarQuest })
        {
            Assert.That(LoadGameScreen.Matches(LoadGameScreen.EFilter.GrimdarkFuture, gf), Is.True, gf.ToString());
            Assert.That(LoadGameScreen.Matches(LoadGameScreen.EFilter.AgeOfFantasy, gf), Is.False, gf.ToString());
        }
        foreach (EGameSystem aof in new[] { EGameSystem.AgeOfFantasy, EGameSystem.AoFSkirmish, EGameSystem.AoFQuest })
        {
            Assert.That(LoadGameScreen.Matches(LoadGameScreen.EFilter.AgeOfFantasy, aof), Is.True, aof.ToString());
            Assert.That(LoadGameScreen.Matches(LoadGameScreen.EFilter.GrimdarkFuture, aof), Is.False, aof.ToString());
        }
        Assert.That(LoadGameScreen.Matches(LoadGameScreen.EFilter.All, null), Is.True,
            "a save whose game can't be read still shows under All.");
        Assert.That(LoadGameScreen.Matches(LoadGameScreen.EFilter.GrimdarkFuture, null), Is.False);
    }

    // ── Map Maker ───────────────────────────────────────────────────────────────────────────────────

    [Test]
    public void TheMapListIsGroupedBySixByFourThenFourByFour()
    {
        var groups = MapMakerScreen.TableGroups();
        Assert.That(groups.Select(g => (g.W, g.H)), Is.EqualTo(new[] { (72f, 48f), (48f, 48f) }));
        Assert.That(groups[0].Games, Does.Contain("Grimdark Future").And.Contain("Age of Fantasy"));
        Assert.That(groups[1].Games, Does.Contain("Firefight").And.Contain("Skirmish").And.Contain("Quest"));
        Assert.That(groups[1].Games, Does.Not.Contain("Grimdark Future,"));
    }

    [Test]
    public void ACopyGetsAFreeNameAndCopyingACopyNumbersIt()
    {
        Assert.That(MapMakerScreen.CopyName("Crossroads", new[] { "Crossroads" }), Is.EqualTo("Crossroads (copy)"));
        Assert.That(MapMakerScreen.CopyName("Crossroads", new[] { "Crossroads", "Crossroads (copy)" }),
            Is.EqualTo("Crossroads (copy 2)"));
        Assert.That(MapMakerScreen.CopyName("Crossroads (copy)", new[] { "Crossroads", "crossroads (COPY)" }),
            Is.EqualTo("Crossroads (copy 2)"), "no \"(copy) (copy)\", and names are matched ignoring case.");
    }

    private sealed class FakeProgress : IGameProgress
    {
        public FakeProgress(int? round, IReadOnlyList<IUnit> unactivated)
        {
            RoundCount = round;
            UnactivatedUnits = unactivated;
        }

        public int? RoundCount { get; }
        public int TotalRounds => 4;
        public IUnit? ActivatingUnit => null;
        public IReadOnlyList<PlayerObjectiveScore> Scores => Array.Empty<PlayerObjectiveScore>();
        public IReadOnlyList<IUnit> UnactivatedUnits { get; }
    }
}
