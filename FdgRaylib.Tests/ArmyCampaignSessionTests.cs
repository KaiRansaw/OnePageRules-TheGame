using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FDG;
using FDG.ArmyBuilding;
using FDG.Campaign;
using FdgRaylib.Forge;
using FdgRaylib.Quest;
using FdgRaylib.SaveLoad;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// The Godot army campaign without the drawing (Army Campaign mockup, approved 2026-11-12): the New Campaign wizard
/// writes both armies linked, Mission Setup fills the rival's force from the difficulty, leaving a unit at home writes
/// the "this mission only" copy, and START MISSION hands the lobby both files and the plan's settings.
/// </summary>
[TestFixture]
public class ArmyCampaignSessionTests
{
    private const EGameSystem Game = EGameSystem.GrimdarkFuture;
    private readonly List<string> _cleanup = new();

    [SetUp]
    public void RequireAnArmiesFolder()
    {
        Assume.That(ArmyPaths.FolderPath, Is.Not.Null, "these tests write to the real campaign folder.");
        MissionSetupSession.Pending = null;
    }

    [TearDown]
    public void DeleteWhatWasWritten()
    {
        MissionSetupSession.Pending = null;
        foreach (string path in _cleanup.SelectMany(p => new[] { p, Path.ChangeExtension(p, ".fdgmission") }))
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        _cleanup.Clear();
    }

    private static ForgeSession? _forge;

    /// <summary>A legal campaign army from a bundled GF book: cheapest non-heroes until the minimum spend.</summary>
    private static BuiltArmyFile Army(string bookName)
    {
        _forge ??= new ForgeSession(ForgeSession.LoadLibraryAsync().Result);
        string slug = GameSystems.CampaignBookSlug(Game)!;
        _forge.ChooseSystem(_forge.Systems.First(x => GameSystems.SameSystem(x, slug)));
        _forge.ChooseBook(_forge.BooksInSystem.First(b => b.Name.Contains(bookName)));
        CampaignNumbers n = CampaignNumbers.For(Game);
        _forge.List.PointsLimit = n.Treasury;
        RosterUnit cheapest = _forge.Book.Units.Where(u => !ArmyCampaignSession.IsHeroRoster(u)).OrderBy(u => u.BasePointCost).First(u => u.BasePointCost > 0);
        while (_forge.Compile().TotalPoints < n.MinimumSpend) _forge.Add(cheapest.Id);
        return _forge.Compile();
    }

    private (ArmyCampaignSession S, string Path) NewCampaign()
    {
        var s = new ArmyCampaignSession(Game, new Random(3));
        var d = new ArmyCampaignSession.Draft
        {
            Name = "Test Wardens " + Guid.NewGuid().ToString("N")[..6], EnemyName = "Test Brood", VpTarget = 5,
            Yours = Army("Battle Brothers"), Enemy = Army("Alien Hives"),
        };
        Assert.That(s.NewProblem(d), Is.Null);
        string? path = s.Finish(d, out string? error);
        Assert.That(error, Is.Null);
        _cleanup.Add(path!);
        _cleanup.Add(s.Find(path!)!.Info.EnemyArmyFile!);
        return (s, path!);
    }

    [Test]
    public void Finish_WritesBothArmies_Linked_WithTheRules()
    {
        var (s, path) = NewCampaign();
        var you = s.Find(path)!;
        var foe = s.EnemyOf(you.Info);
        Assert.That(foe, Is.Not.Null, "the enemy file is written and linked");
        Assert.That(foe!.Info.IsBot, Is.True);
        Assert.That((you.Info.CampaignType, you.Info.VpTarget, foe.Info.VpTarget), Is.EqualTo((ECampaignType.PointLimited, 5, 5)));
        Assert.That(s.Campaigns.Any(c => c.Path == path), "listed under this game");
        Assert.That(s.Campaigns.Any(c => c.Path == foe.Path), Is.False, "the rival is shown inside its campaign, not as one");
        Assert.That(s.ListRow(you).Line, Does.StartWith("0 / 5 VP - 0 missions"));
    }

    [Test]
    public void NewProblem_WalksTheTabs()
    {
        var s = new ArmyCampaignSession(Game);
        var d = new ArmyCampaignSession.Draft();
        Assert.That(s.NewProblem(d), Does.StartWith("Your army:"));
        d.Yours = Army("Battle Brothers");
        Assert.That(s.NewProblem(d), Is.EqualTo("Your army: Name your army."));
        d.Name = "A";
        Assert.That(s.NewProblem(d), Does.StartWith("The enemy:"));
        d.Enemy = Army("Alien Hives");
        d.EnemyName = "a";
        Assert.That(s.NewProblem(d), Is.EqualTo("The enemy: Give the enemy a different name from yours."));
        d.EnemyName = "B";
        d.Planning = ArmyCampaignSession.EPlanning.Ahead;
        Assert.That(s.NewProblem(d), Does.StartWith("Missions:"));
        s.AddPlan(d);
        Assert.That(s.NewProblem(d), Is.Null);
    }

    [Test]
    public void MissionSetup_SizesTheirForce_AndStartWritesTheMissionCopy()
    {
        var (s, path) = NewCampaign();
        var setup = new MissionSetupSession(path, s.OthersThan(s.Find(path)!), new Random(5));
        Assert.That(setup.Plan, Is.Not.Null, "a plan is made for the next mission");
        Assert.That(setup.TheirUnitsComing, Is.GreaterThan(0), "their force is filled from the difficulty on opening");
        Assert.That(setup.TheirsComing, Is.LessThanOrEqualTo(setup.HardCap));
        Assert.That(setup.Blocker(), Is.Null);

        var first = MissionSetupSession.Cards(setup.Army!)[0];
        int before = setup.YourUnitsComing;
        setup.ToggleYours(first.Unit, first.Sheet);
        Assert.That(setup.YourUnitsComing, Is.EqualTo(before - 1));

        ArmyCampaignLaunch? launch = setup.Start(out string? error);
        Assert.That(error, Is.Null);
        Assert.That(MissionSetupSession.Pending, Is.SameAs(launch));
        Assert.That(launch!.ArmyFile, Does.EndWith(".fdgmission"), "one unit stays home, so a copy without it is played");
        Assert.That(StrikeTeamFiles.Load(launch.ArmyFile)!.Units.Count, Is.EqualTo(before - 1));
        Assert.That(launch.CampaignPath, Is.EqualTo(path));
        Assert.That(launch.EnemyArmyFile, Is.Not.Null);
        Assert.That(launch.Title, Does.StartWith(s.Find(path)!.Info.Name + " - Mission 1"));
    }

    [Test]
    public void Recruit_CartPaysFromTheTreasury()
    {
        var (s, path) = NewCampaign();
        var e = s.Find(path)!;
        BookFile book = e.Army.Book!;
        var cards = ForgeSession.BuildRosterCards(book);
        var cart = new ArmyCampaignSession.Cart();
        RosterUnit cheap = book.Units.Where(u => !ArmyCampaignSession.IsHeroRoster(u)).OrderBy(u => u.BasePointCost).First(u => u.BasePointCost > 0);
        int bank = e.Info.BankedPoints;
        int price = cards.TryGetValue(cheap.Id, out var c) ? c.Unit.PointCost : cheap.BasePointCost;
        if (price > bank) { Assert.That(s.WhyNot(e, book, cheap, cart, cards), Does.Contain("treasury")); return; }
        Assert.That(s.WhyNot(e, book, cheap, cart, cards), Is.Null);
        cart.Ids.Add(cheap.Id);
        int units = e.Army.Units.Count;
        s.ConfirmRecruits(e, book, cart);
        var after = s.Find(path)!;
        Assert.That(after.Army.Units.Count, Is.EqualTo(units + 1));
        Assert.That(after.Info.BankedPoints, Is.EqualTo(bank - price));
        Assert.That(cart.Ids, Is.Empty);
    }
}
