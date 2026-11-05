using System;
using System.Linq;
using FDG;
using FDG.ArmyBuilding;
using FDG.Campaign;
using FDG.SaveLoad;
using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// #451 - the locked Army Forge a campaign army is built in (campaign-flow-spec section 3b). Driven
/// through the screen's own seams over the REAL bundled library, because the point of the lock is which
/// books it offers - a hand-made library would only prove the lock works on a library nobody ships.
/// </summary>
[TestFixture]
public class ArmyForgeCampaignBuildTests
{
    private static readonly EGameSystem[] CampaignGames =
    {
        EGameSystem.GrimdarkFuture, EGameSystem.GFFirefight, EGameSystem.AgeOfFantasy, EGameSystem.AoFSkirmish,
    };

    private BuiltArmyFile? _continued;
    private bool _cancelled;

    private ArmyForgeScreen.CampaignBuild Build(EGameSystem system, string title = "Your Army") =>
        new(system, title, CampaignNumbers.For(system), army => _continued = army, () => _cancelled = true);

    [SetUp]
    public void Reset()
    {
        _continued = null;
        _cancelled = false;
    }

    /// <summary>
    /// Adds the book's cheapest non-hero unit until the army is inside the treasury window. Cheapest-first
    /// lands between the minimum spend and the treasury for every bundled campaign book; if one ever
    /// cannot, this says so rather than guessing.
    /// </summary>
    private static void FillToMinimum(ArmyForgeScreen screen, CampaignNumbers n)
    {
        RosterUnit cheapest = screen.CurrentBook.Units
            .Where(u => !u.Rules.Any(r => r is SpecialRuleEntry_Core core
                                          && FDG.Rules.Dispatch.CoreRuleCatalog.IsHeroRuleName(core.Name)))
            .OrderBy(u => u.BasePointCost)
            .First(u => u.BasePointCost > 0);

        for (int i = 0; i < 60 && screen.Compile().TotalPoints < n.MinimumSpend; i++)
            screen.AddToList(cheapest.Id);

        Assume.That(screen.Compile().TotalPoints, Is.InRange(n.MinimumSpend, n.Treasury),
            $"{screen.CurrentBook.Name}'s cheapest unit ({cheapest.BasePointCost} pts) cannot land inside "
            + $"{n.MinimumSpend}-{n.Treasury}; pick another fill strategy for this book.");
    }

    [Test]
    [TestCaseSource(nameof(CampaignGames))]
    public void ABuildOffersOnlyThatGamesBooks(EGameSystem system)
    {
        var screen = new ArmyForgeScreen();
        Assert.That(screen.BeginCampaignBuild(Build(system)), Is.True, $"{system} has bundled books.");

        string slug = GameSystems.CampaignBookSlug(system)!;
        Assert.That(GameSystems.SameSystem(screen.CurrentBook.GameSystem, slug), Is.True,
            "the campaign's own game's book, never another mode's - CLAUDE.md: serving one mode's book "
            + "to another is wrong data.");
        Assert.That(screen.List.PointsLimit, Is.EqualTo(CampaignNumbers.For(system).Treasury),
            "the treasury is the budget.");
        Assert.That(screen.InCampaignBuild, Is.True);
    }

    [Test]
    public void TheQuestGamesCannotBeBuiltHere()
    {
        // Each Quest game is two datasets (heroes and enemy books) with different stats for the same unit,
        // and has its own campaign. Refused outright, and the screen is left as it was.
        foreach (EGameSystem quest in new[] { EGameSystem.GFStarQuest, EGameSystem.AoFQuest })
        {
            var screen = new ArmyForgeScreen();
            Assert.That(screen.BeginCampaignBuild(Build(quest)), Is.False);
            Assert.That(screen.InCampaignBuild, Is.False);
        }
    }

    [Test]
    public void AnEmptyArmyCannotContinue()
    {
        var screen = new ArmyForgeScreen();
        screen.BeginCampaignBuild(Build(EGameSystem.GrimdarkFuture));

        Assert.That(screen.CampaignContinueProblem(screen.Compile()), Is.EqualTo("The army has no units."));
        screen.CampaignContinue();
        Assert.That(_continued, Is.Null, "Continue does nothing while there is a problem.");
    }

    [Test]
    [TestCaseSource(nameof(CampaignGames))]
    public void AnArmyInsideTheTreasuryContinuesWithItsBookAttached(EGameSystem system)
    {
        var screen = new ArmyForgeScreen();
        screen.BeginCampaignBuild(Build(system));
        FillToMinimum(screen, CampaignNumbers.For(system));

        Assert.That(screen.CampaignContinueProblem(screen.Compile()), Is.Null);
        screen.CampaignContinue();

        Assert.That(_continued, Is.Not.Null, "Continue hands the army over.");
        Assert.That(_continued!.Book, Is.Not.Null, "the book rides along - recruiting needs it.");
        Assert.That(_continued.Selections, Is.Not.Null,
            "and the selections - CampaignRules.Start copies each unit's loadout from them, which is what "
            + "lets a unit be upgraded between missions.");
        Assert.That(GameSystems.SameSystem(_continued.GameSystem, GameSystems.CampaignBookSlug(system)), Is.True);
        Assert.That(CampaignRules.CannotStart(_continued, CampaignNumbers.For(system)), Is.Null,
            "what the Forge accepted, the campaign accepts - one rule, not two.");
    }

    [Test]
    public void OverTheTreasuryCannotContinue()
    {
        var screen = new ArmyForgeScreen();
        CampaignNumbers n = CampaignNumbers.For(EGameSystem.GrimdarkFuture);
        screen.BeginCampaignBuild(Build(EGameSystem.GrimdarkFuture));
        FillToMinimum(screen, n);
        RosterUnit any = screen.CurrentBook.Units.OrderBy(u => u.BasePointCost).First(u => u.BasePointCost > 0);
        while (screen.Compile().TotalPoints <= n.Treasury) screen.AddToList(any.Id);

        Assert.That(screen.CampaignContinueProblem(screen.Compile()), Does.Contain("treasury"));
        screen.CampaignContinue();
        Assert.That(_continued, Is.Null);
    }

    /// <summary>
    /// After a campaign build the Forge must be the ordinary Forge again - not half a campaign army with
    /// a treasury for a points limit the next time Kai opens it from the main menu.
    /// </summary>
    [Test]
    public void ContinuingLeavesAnOrdinaryForgeBehind()
    {
        var screen = new ArmyForgeScreen();
        screen.BeginCampaignBuild(Build(EGameSystem.GrimdarkFuture));
        FillToMinimum(screen, CampaignNumbers.For(EGameSystem.GrimdarkFuture));
        screen.CampaignContinue();

        Assert.That(screen.InCampaignBuild, Is.False);
        Assert.That(screen.List.Units, Is.Empty);
        Assert.That(screen.List.PointsLimit, Is.Not.EqualTo(CampaignNumbers.For(EGameSystem.GrimdarkFuture).Treasury));
        Assert.That(_continued!.Selections!.Units, Is.Not.Empty,
            "resetting the screen must not empty the army that was just handed over.");
    }

    [Test]
    public void BackCancelsAndResets()
    {
        var screen = new ArmyForgeScreen();
        screen.BeginCampaignBuild(Build(EGameSystem.AgeOfFantasy));
        FillToMinimum(screen, CampaignNumbers.For(EGameSystem.AgeOfFantasy));

        screen.CampaignCancel();

        Assert.That(_cancelled, Is.True);
        Assert.That(_continued, Is.Null);
        Assert.That(screen.InCampaignBuild, Is.False);
        Assert.That(screen.List.Units, Is.Empty);
    }

    [Test]
    public void TheHeaderNamesTheArmyTheGameAndTheWallet()
    {
        var screen = new ArmyForgeScreen();
        ArmyForgeScreen.CampaignBuild build = Build(EGameSystem.GrimdarkFuture, "Enemy Army");
        screen.BeginCampaignBuild(build);

        string header = ArmyForgeScreen.CampaignHeader(build, screen.Compile());
        Assert.That(header, Is.EqualTo("Enemy Army  -  Grimdark Future  -  0 / 750 pts spent  -  minimum 600  -  Heroes 0/1"));
        Assert.That(header.All(c => c <= 127), Is.True, "game text is ASCII-only.");
    }
}
