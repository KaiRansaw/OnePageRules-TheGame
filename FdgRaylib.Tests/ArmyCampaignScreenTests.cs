using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FDG;
using FDG.ArmyBuilding;
using FDG.Campaign;
using FDG.SaveLoad;
using FdgRaylib.Quest;
using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// UI rework batch F - the army campaign screens (wish list, "Army Campaign + Mission Setup"): the list is
/// this game's only, Delete removes both armies, roster cards are tagged, Recruit greys out a second Hero,
/// and Mission Setup's force cards bring / leave a unit. Nothing here draws a frame; the wording and the
/// rules behind each button are pure and pinned.
/// </summary>
[TestFixture]
public class ArmyCampaignScreenTests
{
    private const EGameSystem Game = EGameSystem.GrimdarkFuture;
    private readonly List<string> _cleanup = new();

    [TearDown]
    public void DeleteWhatWasWritten()
    {
        foreach (string path in _cleanup)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        _cleanup.Clear();
    }

    /// <summary>A started campaign army of <paramref name="system"/> with one plain unit, written to disk.</summary>
    private string WriteCampaign(EGameSystem system, string name, bool bot, string? enemyPath = null)
    {
        var army = new BuiltArmyFile { Name = name, Faction = "Test" };
        army.Units.Add(new UnitFileEntry { Name = "Warriors", ModelCount = 5, PointCost = 100 });
        string path = StrikeTeamFiles.NewPath(system, name);
        CampaignRules.Start(army, name, path, CampaignNumbers.For(system), system, isBot: bot);
        army.StrikeTeam!.EnemyArmyFile = enemyPath;
        Assert.That(StrikeTeamFiles.Save(path, army), Is.True);
        _cleanup.Add(path);
        return path;
    }

    [Test]
    public void TheListShowsOnlyTheGameThePlayerCameFrom()
    {
        Assume.That(ArmyPaths.FolderPath, Is.Not.Null, "writes to the real campaign folder; none was found.");
        string tag = Guid.NewGuid().ToString("N")[..6];
        string gf = WriteCampaign(EGameSystem.GrimdarkFuture, $"zz GF {tag}", false);
        string aof = WriteCampaign(EGameSystem.AgeOfFantasy, $"zz AoF {tag}", false);

        var screen = new StrikeTeamScreen();
        screen.Open(EGameSystem.GrimdarkFuture);
        screen.OnShown();
        var listed = screen.ListedPathsForTest.Select(Path.GetFullPath).ToList();
        Assert.That(listed, Does.Contain(Path.GetFullPath(gf)));
        Assert.That(listed, Does.Not.Contain(Path.GetFullPath(aof)), "an Age of Fantasy campaign is not listed under Grimdark Future");

        screen.Open(EGameSystem.AgeOfFantasy);
        screen.OnShown();
        listed = screen.ListedPathsForTest.Select(Path.GetFullPath).ToList();
        Assert.That(listed, Does.Contain(Path.GetFullPath(aof)));
        Assert.That(listed, Does.Not.Contain(Path.GetFullPath(gf)));
    }

    [Test]
    public void DeleteRemovesBothArmies()
    {
        Assume.That(ArmyPaths.FolderPath, Is.Not.Null, "writes to the real campaign folder; none was found.");
        string tag = Guid.NewGuid().ToString("N")[..6];
        string theirs = WriteCampaign(Game, $"zz Foe {tag}", true);
        string mine = WriteCampaign(Game, $"zz Me {tag}", false, theirs);
        string missionCopy = Path.ChangeExtension(theirs, ".fdgmission");
        File.WriteAllText(missionCopy, "{}");
        _cleanup.Add(missionCopy);

        StrikeTeamInfo info = StrikeTeamFiles.Load(mine)!.StrikeTeam!;
        Assert.That(StrikeTeamScreen.CampaignFiles(mine, info), Does.Contain(theirs).And.Contain(mine).And.Contain(missionCopy));

        var screen = new StrikeTeamScreen();
        screen.Open(Game);
        screen.OnShown();
        screen.DeleteCampaign(mine, info);
        Assert.That(File.Exists(mine), Is.False);
        Assert.That(File.Exists(theirs), Is.False, "the old Delete left the enemy's file behind");
        Assert.That(File.Exists(missionCopy), Is.False);
    }

    [Test]
    public void ListRowSaysTheScoreAndWhatIsWaiting()
    {
        var info = new StrikeTeamInfo { Name = "Reavers", Vp = 4, Matches = 4, CampaignType = ECampaignType.PointLimited, VpTarget = 7 };
        var foe = new StrikeTeamInfo { Name = "Choir", IsBot = true, CampaignType = ECampaignType.PointLimited, VpTarget = 7 };
        var standing = CampaignPreGame.Standing(info, foe);
        (string title, string line) = StrikeTeamScreen.ListRow(info, foe, standing, choicesWaiting: true);
        Assert.That(title, Is.EqualTo("Reavers vs Choir"));
        Assert.That(line, Is.EqualTo("4 / 7 VP - 4 mission(s) - choices waiting"));

        var endless = new StrikeTeamInfo { Name = "A", Vp = 6, Matches = 9 };
        (_, string endlessLine) = StrikeTeamScreen.ListRow(endless, null, CampaignPreGame.Standing(endless, null), false);
        Assert.That(endlessLine, Is.EqualTo("Endless - 6 VP - 9 mission(s)"));
    }

    [Test]
    public void RosterCardsAreTaggedPickATraitOrRecovering()
    {
        Assert.That(StrikeTeamScreen.RosterTag(new StrikeUnit { Pending = { CampaignTables.FastLearner } }), Is.EqualTo("pick a trait"));
        Assert.That(StrikeTeamScreen.RosterTag(new StrikeUnit { MissionsOut = 2 }), Is.EqualTo("recovering (2)"));
        Assert.That(StrikeTeamScreen.RosterTag(new StrikeUnit()), Is.Null);
        Assert.That(StrikeTeamScreen.RosterTag(null), Is.Null);
    }

    /// <summary>Campaign Rules v3.6.0, 6. Recruit Units: one new Hero after each mission - a Hero in the cart
    /// greys out the other Heroes; regular units stay unlimited.</summary>
    [Test]
    public void AHeroInTheCartGreysOutOtherHeroes()
    {
        var forge = new ArmyForgeScreen();
        CampaignNumbers n = CampaignNumbers.For(Game);
        BuiltArmyFile? built = null;
        Assert.That(forge.BeginCampaignBuild(new ArmyForgeScreen.CampaignBuild(Game, "x", n, a => built = a, () => { })), Is.True);
        BookFile book = forge.CurrentBook;
        bool IsHero(RosterUnit u) => u.Rules.Any(r => r is SpecialRuleEntry_Core c && FDG.Rules.Dispatch.CoreRuleCatalog.IsHeroRuleName(c.Name));
        RosterUnit hero = book.Units.Where(IsHero).OrderBy(u => u.BasePointCost).First();
        RosterUnit plain = book.Units.Where(u => !IsHero(u) && u.BasePointCost > 0).OrderBy(u => u.BasePointCost).First();
        while (forge.Compile().TotalPoints < n.MinimumSpend) forge.AddToList(plain.Id);
        forge.CampaignContinue();
        BuiltArmyFile army = built!;
        CampaignRules.Start(army, "zz Recruit", "x.fdgarmy", n, Game);
        army.StrikeTeam!.BankedPoints = 2000;

        Assert.That(StrikeTeamScreen.CannotAddToCart(army, hero, hero.BasePointCost, new List<bool>(), 0), Is.Null);
        Assert.That(StrikeTeamScreen.CannotAddToCart(army, hero, hero.BasePointCost, new List<bool> { true }, hero.BasePointCost),
            Does.Contain("one new Hero"));
        Assert.That(StrikeTeamScreen.CannotAddToCart(army, plain, plain.BasePointCost, new List<bool> { true, false, false }, 500),
            Is.Null, "regular units are unlimited");
        Assert.That(StrikeTeamScreen.CannotAddToCart(army, plain, plain.BasePointCost, new List<bool>(), 2000),
            Does.Contain("treasury"), "the cart's points come out of the treasury first");

        // The allowance spent on the file (a Hero bought since the last mission) greys them out too.
        army.StrikeTeam.HeroRecruitedAfterMission = army.StrikeTeam.Matches;
        Assert.That(StrikeTeamScreen.CannotAddToCart(army, hero, hero.BasePointCost, new List<bool>(), 0), Does.Contain("one new Hero"));
    }

    [Test]
    public void ForceCardsBringAndLeaveUnits()
    {
        var army = new BuiltArmyFile { Name = "F" };
        army.Units.Add(new UnitFileEntry { Name = "A", ModelCount = 1, PointCost = 50 });
        army.Units.Add(new UnitFileEntry { Name = "B", ModelCount = 1, PointCost = 50 });
        army.Units.Add(new UnitFileEntry { Name = "C", ModelCount = 1, PointCost = 50 });
        CampaignRules.Start(army, "F", "f.fdgarmy", CampaignNumbers.For(Game), Game, noPermadeath: true);
        StrikeTeamInfo info = army.StrikeTeam!;
        StrikeUnit a = CampaignRules.Sheet(army.Units[0])!, b = CampaignRules.Sheet(army.Units[1])!, c = CampaignRules.Sheet(army.Units[2])!;
        c.MissionsOut = 1;
        CampaignRules.Save(army.Units[2], c);

        Assert.That(MissionSetupScreen.ForceTag(info, army.Units[0], a), Is.EqualTo("coming"), "an empty list means everyone comes");
        Assert.That(MissionSetupScreen.ForceTag(info, army.Units[2], c), Is.EqualTo("recovering (1)"));

        MissionSetupScreen.ToggleComing(army, info, b);
        Assert.That(MissionSetupScreen.ForceTag(info, army.Units[1], b), Is.EqualTo("stays home"));
        Assert.That(MissionSetupScreen.ForceTag(info, army.Units[0], a), Is.EqualTo("coming"), "leaving one at home keeps the rest");
        MissionSetupScreen.ToggleComing(army, info, b);
        Assert.That(MissionSetupScreen.ForceTag(info, army.Units[1], b), Is.EqualTo("coming"));
    }
}
