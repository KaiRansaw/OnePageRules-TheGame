using System;
using System.Collections.Generic;
using System.Linq;
using FDG.ArmyBuilding;
using FDG.SaveLoad;
using FdgRaylib.Rendering;
using NUnit.Framework;
using Cat = FdgRaylib.Rendering.ForgeRosterCategories.ECategory;

namespace FdgRaylib.Tests;

/// <summary>
/// UI rework batch C (2026-11-07): the Army Forge screen. What is pinned is what the screen DECIDES - which
/// box a unit goes in and in what order, what the points bar says, what New does - not how ImGui draws it.
///
/// <para>The category rules are Army Forge's own (read from its published front-end on 2026-11-07, and
/// checked against all 5231 units of the 298 official books of the six systems with zero differences - see
/// <see cref="ForgeRosterCategories"/>).</para>
/// </summary>
[TestFixture]
public class UiReworkBatchCTests
{
    private static RosterUnit Unit(string name, int models, int cost, params SpecialRuleEntry[] rules) => new()
    {
        Id = name,
        Name = name,
        Quality = 4,
        Defense = 4,
        BaseModelCount = models,
        MinModels = models,
        MaxModels = models,
        BasePointCost = cost,
        Rules = rules.ToList(),
    };

    private static SpecialRuleEntry Rule(string name) => new SpecialRuleEntry_Core(name);
    private static SpecialRuleEntry Tough(int value) => new SpecialRuleEntry_CoreNumeric("Tough", value);

    // ── Which box ────────────────────────────────────────────────────────────────────────────────────

    [Test]
    public void EachRuleSendsAUnitToArmyForgesBox()
    {
        string gf = GameSystems.GrimdarkFuture;
        Assert.That(ForgeRosterCategories.Classify(Unit("A", 1, 100, Rule("Unique Hero"), Tough(6)), gf), Is.EqualTo(Cat.NarrativeHeroes));
        Assert.That(ForgeRosterCategories.Classify(Unit("B", 1, 100, Rule("Hero"), Tough(12)), gf), Is.EqualTo(Cat.Heroes));
        Assert.That(ForgeRosterCategories.Classify(Unit("C", 1, 100, Rule("Aircraft"), Tough(18)), gf), Is.EqualTo(Cat.Aircraft));
        Assert.That(ForgeRosterCategories.Classify(Unit("D", 1, 100, Rule("Artillery"), Tough(18)), gf), Is.EqualTo(Cat.Artillery));
        Assert.That(ForgeRosterCategories.Classify(Unit("E", 1, 100, Rule("Entrenched")), gf), Is.EqualTo(Cat.Artillery));
        Assert.That(ForgeRosterCategories.Classify(Unit("F", 1, 100, Rule("Mobile Artillery")), gf), Is.EqualTo(Cat.Artillery));
        Assert.That(ForgeRosterCategories.Classify(Unit("G", 1, 100, Tough(18)), gf), Is.EqualTo(Cat.Titans));
        Assert.That(ForgeRosterCategories.Classify(Unit("H", 1, 100, Tough(9)), gf), Is.EqualTo(Cat.VehiclesMonsters));
        Assert.That(ForgeRosterCategories.Classify(Unit("I", 1, 100, Tough(6)), gf), Is.EqualTo(Cat.LightVehiclesSmallMonsters));
        Assert.That(ForgeRosterCategories.Classify(Unit("J", 1, 100, Tough(3)), gf), Is.EqualTo(Cat.CoreUnits));
        Assert.That(ForgeRosterCategories.Classify(Unit("K", 10, 100), gf), Is.EqualTo(Cat.CoreUnits));
    }

    [Test]
    public void OnlyASingleModelCanBeAVehicleOrMonster()
    {
        // Army Forge's size check: three Tough(12) beasts are Core Units, one is Vehicles / Monsters.
        Assert.That(ForgeRosterCategories.Classify(Unit("Beasts", 3, 300, Tough(12)), GameSystems.GrimdarkFuture),
            Is.EqualTo(Cat.CoreUnits));
        Assert.That(ForgeRosterCategories.Classify(Unit("Beast", 1, 100, Tough(12)), GameSystems.GrimdarkFuture),
            Is.EqualTo(Cat.VehiclesMonsters));
    }

    [Test]
    public void FirefightAndSkirmishTripleTheToughThresholds()
    {
        foreach (string system in new[] { GameSystems.GFFirefight, GameSystems.AoFSkirmish })
        {
            Assert.That(ForgeRosterCategories.Classify(Unit("A", 1, 1, Tough(9)), system), Is.EqualTo(Cat.CoreUnits), system);
            Assert.That(ForgeRosterCategories.Classify(Unit("B", 1, 1, Tough(18)), system), Is.EqualTo(Cat.LightVehiclesSmallMonsters), system);
            Assert.That(ForgeRosterCategories.Classify(Unit("C", 1, 1, Tough(27)), system), Is.EqualTo(Cat.VehiclesMonsters), system);
            Assert.That(ForgeRosterCategories.Classify(Unit("D", 1, 1, Tough(54)), system), Is.EqualTo(Cat.Titans), system);
        }
    }

    [Test]
    public void QuestDividesTheUnitsToughByThree()
    {
        foreach (string system in new[] { GameSystems.GFStarQuest, GameSystems.AoFQuest })
        {
            Assert.That(ForgeRosterCategories.Classify(Unit("A", 1, 1, Tough(9)), system), Is.EqualTo(Cat.CoreUnits), system);
            Assert.That(ForgeRosterCategories.Classify(Unit("B", 1, 1, Tough(18)), system), Is.EqualTo(Cat.LightVehiclesSmallMonsters), system);
            Assert.That(ForgeRosterCategories.Classify(Unit("C", 1, 1, Tough(27)), system), Is.EqualTo(Cat.VehiclesMonsters), system);
            Assert.That(ForgeRosterCategories.Classify(Unit("D", 1, 1, Tough(54)), system), Is.EqualTo(Cat.Titans), system);
        }
    }

    // ── Box order and order inside a box ─────────────────────────────────────────────────────────────

    [Test]
    public void BoxesComeInArmyForgesOrderAndEmptyOnesAreLeftOut()
    {
        var book = new BookFile
        {
            Name = "Test",
            GameSystem = GameSystems.GrimdarkFuture,
            Units =
            {
                Unit("Flyer", 1, 200, Rule("Aircraft")),
                Unit("Titan", 1, 600, Tough(18)),
                Unit("Squad", 10, 100),
                Unit("Captain", 1, 80, Rule("Hero")),
            },
        };
        var groups = ForgeRosterCategories.GroupBook(book);
        Assert.That(groups.Select(g => g.Title),
            Is.EqualTo(new[] { "Heroes", "Core Units", "Titans", "Aircraft" }));
    }

    [Test]
    public void HeroesRunMostExpensiveFirst_OtherBoxesBiggestThenCheapest()
    {
        var book = new BookFile
        {
            Name = "Test",
            GameSystem = GameSystems.GrimdarkFuture,
            Units =
            {
                Unit("Cheap Hero", 1, 50, Rule("Hero")),
                Unit("Dear Hero", 1, 150, Rule("Hero")),
                Unit("Small Dear", 3, 200),
                Unit("Big Dear", 10, 180),
                Unit("Big Cheap", 10, 90),
                Unit("Small Cheap", 3, 60),
            },
        };
        var groups = ForgeRosterCategories.GroupBook(book);
        Assert.That(groups[0].Units.Select(u => u.Name), Is.EqualTo(new[] { "Dear Hero", "Cheap Hero" }));
        Assert.That(groups[1].Units.Select(u => u.Name),
            Is.EqualTo(new[] { "Big Cheap", "Big Dear", "Small Cheap", "Small Dear" }));
    }

    // ── Real books ───────────────────────────────────────────────────────────────────────────────────

    [Test]
    public void AlienHivesSortsTheWayArmyForgeSortsIt()
    {
        BookFile hives = BookLibrary.Load().First(b => b.Name == "Alien Hives"
            && GameSystems.SameSystem(b.GameSystem, GameSystems.GrimdarkFuture));
        Cat Of(string name) => ForgeRosterCategories.Classify(hives.Units.First(u => u.Name == name), hives.GameSystem);

        // Each pinned against army-forge.onepagerules.com's list builder, 2026-11-07.
        Assert.That(Of("Drekhor the Vengeful"), Is.EqualTo(Cat.NarrativeHeroes));
        Assert.That(Of("Hive Lord"), Is.EqualTo(Cat.Heroes));
        Assert.That(Of("Assault Grunts"), Is.EqualTo(Cat.CoreUnits));
        Assert.That(Of("Hive Warriors"), Is.EqualTo(Cat.CoreUnits));
        Assert.That(Of("Mortar Beast"), Is.EqualTo(Cat.LightVehiclesSmallMonsters));
        Assert.That(Of("Carnivo-Rex"), Is.EqualTo(Cat.VehiclesMonsters));
        Assert.That(Of("Invasion Artillery Spore"), Is.EqualTo(Cat.Artillery));
        Assert.That(Of("Hive Titan"), Is.EqualTo(Cat.Titans));
        Assert.That(Of("Rapacious Beast"), Is.EqualTo(Cat.Aircraft));
    }

    [Test]
    public void EveryShippedUnitLandsInExactlyOneBox()
    {
        foreach (BookFile book in BookLibrary.Load())
        {
            var grouped = ForgeRosterCategories.GroupBook(book).SelectMany(g => g.Units).ToList();
            Assert.That(grouped.Count, Is.EqualTo(book.Units.Count), $"{book.GameSystem} / {book.Name}");
            Assert.That(grouped.Select(u => u.Id).Distinct().Count(), Is.EqualTo(book.Units.Count), $"{book.GameSystem} / {book.Name}");
        }
    }

    [Test]
    public void EveryShippedRosterUnitHasACardAtItsBookPoints()
    {
        foreach (BookFile book in BookLibrary.Load())
        {
            var cards = ArmyForgeScreen.BuildRosterCards(book);
            foreach (RosterUnit unit in book.Units)
            {
                Assert.That(cards.ContainsKey(unit.Id), $"{book.Name} / {unit.Name} has no card");
                Assert.That(cards[unit.Id].Unit.Name, Is.EqualTo(unit.Name));
                Assert.That(cards[unit.Id].Unit.PointCost, Is.EqualTo(unit.BasePointCost),
                    $"{book.Name} / {unit.Name}: the card's points are the book's");
            }
        }
    }

    [Test]
    public void BoxTitlesAreAscii()
    {
        foreach (Cat category in Enum.GetValues<Cat>())
            Assert.That(ForgeRosterCategories.Title(category).All(c => c < 128), category.ToString());
        Assert.That(ArmyForgeScreen.CampaignFactionLockedTip.All(c => c < 128));
    }

    // ── Points bar ───────────────────────────────────────────────────────────────────────────────────

    [Test]
    public void ThePointsBarFillsToTheLimit()
    {
        var bar = ArmyForgeScreen.PointsBar(500, 2000);
        Assert.That(bar.Fraction, Is.EqualTo(0.25f));
        Assert.That(bar.Over, Is.False);
        Assert.That(bar.Left, Is.EqualTo(1500));
        Assert.That(bar.Label, Is.EqualTo("500 / 2000 pts"));
    }

    [Test]
    public void ExactlyAtTheLimitIsNotOver()
    {
        var bar = ArmyForgeScreen.PointsBar(2000, 2000);
        Assert.That(bar.Over, Is.False);
        Assert.That(bar.Fraction, Is.EqualTo(1f));
        Assert.That(bar.Left, Is.EqualTo(0));
    }

    [Test]
    public void OverTheLimitIsFullAndSaysByHowMuch()
    {
        var bar = ArmyForgeScreen.PointsBar(2150, 2000);
        Assert.That(bar.Over, Is.True);
        Assert.That(bar.OverBy, Is.EqualTo(150));
        Assert.That(bar.Fraction, Is.EqualTo(1f));
        Assert.That(bar.Label, Is.EqualTo("OVER LIMIT by 150 pts"));
    }

    [Test]
    public void NoLimitDoesNotDivideByZero()
    {
        var bar = ArmyForgeScreen.PointsBar(300, 0);
        Assert.That(bar.Fraction, Is.EqualTo(0f));
        Assert.That(bar.Over, Is.False);
        Assert.That(bar.Label, Is.EqualTo("300 pts"));
    }

    [Test]
    public void TheBadgeSaysLegalOrCountsProblems()
    {
        Assert.That(ArmyForgeScreen.LegalityBadge(Array.Empty<ListIssue>()), Is.EqualTo("Legal"));
        Assert.That(ArmyForgeScreen.LegalityBadge(new[] { new ListIssue("w", ListIssueSeverity.Warning) }),
            Is.EqualTo("1 warning"));
        Assert.That(ArmyForgeScreen.LegalityBadge(new[]
        {
            new ListIssue("w", ListIssueSeverity.Warning),
            new ListIssue("e1", ListIssueSeverity.Error),
            new ListIssue("e2", ListIssueSeverity.Error),
        }), Is.EqualTo("2 errors"));
    }

    [Test]
    public void TheArmyBadgeSaysOverLimitThroughTheValidator()
    {
        var screen = new ArmyForgeScreen(DemoBook.Build());
        screen.List.PointsLimit = 1;
        screen.AddToList(DemoBook.Build().Units[0].Id);
        Assert.That(ArmyForgeScreen.LegalityBadge(screen.Issues()), Does.Contain("error"));
    }

    // ── Your Army strip ──────────────────────────────────────────────────────────────────────────────

    [Test]
    public void TheStripWrapsToASecondRowThenScrolls()
    {
        Assert.That(ArmyForgeScreen.VisibleStripRows(0), Is.EqualTo(1), "empty army: one row for the hint");
        Assert.That(ArmyForgeScreen.VisibleStripRows(1), Is.EqualTo(1));
        Assert.That(ArmyForgeScreen.VisibleStripRows(2), Is.EqualTo(2));
        Assert.That(ArmyForgeScreen.VisibleStripRows(7), Is.EqualTo(2), "more than two rows scroll");
    }

    [Test]
    public void CardRowsCountTheWrap()
    {
        // 100 wide cards, 6 gap: 3 fit in 312.
        Assert.That(UnitPortraitCards.RowsFor(0, 312f, 100f), Is.EqualTo(0));
        Assert.That(UnitPortraitCards.RowsFor(3, 312f, 100f), Is.EqualTo(1));
        Assert.That(UnitPortraitCards.RowsFor(4, 312f, 100f), Is.EqualTo(2));
        Assert.That(UnitPortraitCards.RowsFor(10, 50f, 100f), Is.EqualTo(10), "never fewer than one per row");
    }

    [Test]
    public void RosterBoxesSplitIntoTwoEvenColumnsInOrder()
    {
        var cols = ArmyForgeScreen.SplitIntoColumns(new[] { 4, 10, 3, 3, 2 });
        // Each box counts its cards + 1 for its title: 5 | 11, then 9, 13 on the left, then 14 on the right.
        Assert.That(cols[0], Is.EqualTo(new[] { 0, 2, 3 }));
        Assert.That(cols[1], Is.EqualTo(new[] { 1, 4 }));
        Assert.That(cols.SelectMany(c => c).OrderBy(i => i), Is.EqualTo(new[] { 0, 1, 2, 3, 4 }));
    }

    // ── New ──────────────────────────────────────────────────────────────────────────────────────────

    [Test]
    public void NewClearsTheArmyButKeepsTheGameFactionAndLimit()
    {
        BookFile book = DemoBook.Build();
        var screen = new ArmyForgeScreen(book);
        screen.List.PointsLimit = 1750;
        screen.List.Name = "Old Lads";
        screen.AddToList(book.Units[0].Id);

        screen.NewList();

        Assert.That(screen.List.Units, Is.Empty);
        Assert.That(screen.List.Name, Is.Empty);
        Assert.That(screen.List.PointsLimit, Is.EqualTo(1750));
        Assert.That(screen.CurrentBook.Name, Is.EqualTo(book.Name));
        Assert.That(screen.Status.Text, Is.EqualTo("New army started."));
    }

    [Test]
    public void NewIsADeliberateStartSoAFailedLoadNoLongerGuardsSave()
    {
        BookFile book = DemoBook.Build();
        var screen = new ArmyForgeScreen(book);
        screen.AddToList(book.Units[0].Id);
        Assert.That(screen.TryAdopt(null, "broken.fdgarmy", "bad json"), Is.EqualTo(ELoadOutcome.Rejected));
        Assert.That(screen.PendingSaveGuard(), Is.EqualTo(ESaveGuard.UnchangedAfterFailedLoad));

        screen.NewList();

        // Still guarded - but by the empty list, the honest reason now.
        Assert.That(screen.PendingSaveGuard(), Is.EqualTo(ESaveGuard.EmptyList));
    }
}
