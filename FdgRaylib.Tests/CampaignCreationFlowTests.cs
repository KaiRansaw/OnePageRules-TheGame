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
/// #452 - making a campaign, end to end through the campaign screen's own wizard: setup, your army built in
/// the locked Army Forge, named, the enemy built or picked, named, Finish. Kai's answers of 2026-11-03.
///
/// <para>
/// The Army Forge itself is real (a real <see cref="ArmyForgeScreen"/> over the bundled books builds the
/// armies); only the NAVIGATION between the two screens is simulated - exactly what Program.cs does: run
/// the build's callback, then show the campaign screen again. That second half is the thing most likely to
/// break, because showing the campaign screen normally resets it to the list, which would throw away a
/// campaign that is half made.
/// </para>
/// </summary>
[TestFixture]
public class CampaignCreationFlowTests
{
    private const EGameSystem Game = EGameSystem.GrimdarkFuture;
    private readonly List<string> _cleanup = new();

    [SetUp]
    public void RequireAnArmiesFolder() =>
        Assume.That(ArmyPaths.FolderPath, Is.Not.Null,
            "these tests write to the real campaign folder; none was found from the test host.");

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

    /// <summary>A legal campaign army, built in the real locked Forge from a bundled book.</summary>
    private static BuiltArmyFile ForgeArmy()
    {
        BuiltArmyFile? built = null;
        var forge = new ArmyForgeScreen();
        CampaignNumbers n = CampaignNumbers.For(Game);
        Assert.That(forge.BeginCampaignBuild(new ArmyForgeScreen.CampaignBuild(Game, "x", n, a => built = a, () => { })), Is.True);
        RosterUnit cheapest = forge.CurrentBook.Units
            .Where(u => !u.Rules.Any(r => r is SpecialRuleEntry_Core c && FDG.Rules.Dispatch.CoreRuleCatalog.IsHeroRuleName(c.Name)))
            .OrderBy(u => u.BasePointCost).First(u => u.BasePointCost > 0);
        while (forge.Compile().TotalPoints < n.MinimumSpend) forge.AddToList(cheapest.Id);
        forge.CampaignContinue();
        Assert.That(built, Is.Not.Null);
        return built!;
    }

    /// <summary>
    /// A campaign screen whose "open the Army Forge" hands back the given armies in turn, the way Program.cs
    /// routes it: the build's callback runs, then the screen is shown again.
    /// </summary>
    private static StrikeTeamScreen ScreenThatBuilds(Queue<BuiltArmyFile> armies, List<string> titles)
    {
        var screen = new StrikeTeamScreen();
        screen.Open(Game);
        screen.OnBuildArmy = build =>
        {
            titles.Add(build.Title);
            build.OnContinue(armies.Dequeue());
            screen.OnShown();
            return true;
        };
        screen.OnShown();
        return screen;
    }

    private string Remember(string? path)
    {
        Assert.That(path, Is.Not.Null);
        _cleanup.Add(path!);
        return path!;
    }

    [Test]
    public void BuildingBothArmiesMakesALinkedCampaign()
    {
        string tag = Guid.NewGuid().ToString("N")[..6];
        var titles = new List<string>();
        StrikeTeamScreen screen = ScreenThatBuilds(new Queue<BuiltArmyFile>(new[] { ForgeArmy(), ForgeArmy() }), titles);

        screen.StartNew();
        Assert.That(screen.NewTabForTest, Is.EqualTo(StrikeTeamScreen.ENewTab.Rules), "the wizard opens on 1. Rules.");
        screen.SetEndForTest(ECampaignType.PointLimited, 6);
        screen.OpeningNoteForTest = "The Reavers swore an oath.";

        // 2. Your army - built in the Forge, which comes back to the wizard on the same tab.
        screen.BuildYourArmy();
        Assert.That(screen.ViewForTest, Is.EqualTo(StrikeTeamScreen.EView.New),
            "coming back from the Forge keeps the wizard - it must not reset to the list.");
        Assert.That(screen.NewTabForTest, Is.EqualTo(StrikeTeamScreen.ENewTab.YourArmy));
        screen.NewNameForTest = $"zz Reavers {tag}";
        Assert.That(screen.YourNameProblem(), Is.Null);

        // 3. The enemy.
        Assert.That(screen.NewProblem(), Does.StartWith("The enemy:"));
        screen.BuildEnemy();
        Assert.That(screen.NewTabForTest, Is.EqualTo(StrikeTeamScreen.ENewTab.Enemy));
        Assert.That(titles, Is.EqualTo(new[] { "Your Army", "Enemy Army" }), "the Forge is told which army it is building.");
        screen.EnemyNameForTest = $"zz Choir {tag}";
        Assert.That(screen.EnemyNameProblem(), Is.Null);
        Assert.That(screen.NewProblem(), Is.Null, "plan as I go needs nothing more");

        screen.Finish();

        Assert.That(screen.ViewForTest, Is.EqualTo(StrikeTeamScreen.EView.Camp), "START CAMPAIGN opens the new campaign.");
        string mine = Remember(screen.CurrentPathForTest);
        StrikeTeamInfo you = StrikeTeamFiles.Load(mine)!.StrikeTeam!;
        string theirsPath = Remember(you.EnemyArmyFile);
        StrikeTeamInfo rival = StrikeTeamFiles.Load(theirsPath)!.StrikeTeam!;

        Assert.That(you.Name, Is.EqualTo($"zz Reavers {tag}"));
        Assert.That(rival.Name, Is.EqualTo($"zz Choir {tag}"));
        Assert.That(you.IsBot, Is.False);
        Assert.That(rival.IsBot, Is.True, "the enemy is the bot's army.");
        Assert.That(you.GameSystem, Is.EqualTo(Game), "the game is the one the screen was opened for - no picker.");
        Assert.That(you.VpTarget, Is.EqualTo(6));
        Assert.That(rival.VpTarget, Is.EqualTo(6), "both armies carry the campaign's end condition.");
        Assert.That(you.Journal, Is.EqualTo("The Reavers swore an oath."), "the opening note is the first journal page.");
        Assert.That(you.Plans, Is.Empty, "plan as I go: nothing planned up front.");
        Assert.That(Path.GetDirectoryName(mine), Is.EqualTo(Path.GetDirectoryName(theirsPath)));
        Assert.That(mine, Does.Contain(ArmyPaths.CampaignsDirectoryName), "campaign armies live in their own folder (#447).");
    }

    /// <summary>Tab 4, "Plan missions ahead": the planned missions and their briefings go into the campaign.</summary>
    [Test]
    public void MissionsPlannedAheadGoIntoTheCampaign()
    {
        string tag = Guid.NewGuid().ToString("N")[..6];
        StrikeTeamScreen screen = ScreenThatBuilds(new Queue<BuiltArmyFile>(new[] { ForgeArmy(), ForgeArmy() }), new List<string>());
        screen.StartNew();
        screen.BuildYourArmy();
        screen.NewNameForTest = $"zz Planner {tag}";
        screen.BuildEnemy();
        screen.EnemyNameForTest = $"zz Planned Foe {tag}";

        screen.PlanningForTest = StrikeTeamScreen.EMissionPlanning.Ahead;
        Assert.That(screen.NewProblem(), Does.StartWith("Missions:"), "planning ahead with nothing planned is not finished");
        int d = 0;
        Func<int> dice = () => (d++ % 6) + 1;
        screen.DraftPlansForTest.Add(CampaignMissionGenerator.Generate(1, Game, dice));
        screen.DraftPlansForTest.Add(CampaignMissionGenerator.Generate(2, Game, dice));
        screen.DraftPlansForTest[1].Notes = "Hold the bridge.";
        Assert.That(screen.NewProblem(), Is.Null);

        screen.Finish();
        string mine = Remember(screen.CurrentPathForTest);
        StrikeTeamInfo you = StrikeTeamFiles.Load(mine)!.StrikeTeam!;
        Remember(you.EnemyArmyFile);
        Assert.That(you.Plans.Select(p => p.Mission), Is.EqualTo(new[] { 1, 2 }));
        Assert.That(CampaignJournal.NoteFor(you, 2), Is.EqualTo("Hold the bridge."), "a planned briefing becomes that mission's journal page.");
    }

    [Test]
    public void TheOpeningNoteCanBeSkipped()
    {
        string tag = Guid.NewGuid().ToString("N")[..6];
        StrikeTeamScreen screen = ScreenThatBuilds(new Queue<BuiltArmyFile>(new[] { ForgeArmy(), ForgeArmy() }), new List<string>());

        screen.StartNew();
        screen.BuildYourArmy();
        screen.NewNameForTest = $"zz Quiet {tag}";
        screen.BuildEnemy();
        screen.EnemyNameForTest = $"zz Foe {tag}";
        screen.Finish();

        string mine = Remember(screen.CurrentPathForTest);
        StrikeTeamInfo you = StrikeTeamFiles.Load(mine)!.StrikeTeam!;
        Remember(you.EnemyArmyFile);
        Assert.That(you.Journal, Is.Empty, "skipping the story leaves the journal empty, not missing.");
    }

    /// <summary>"Pick one for me": an army from the folder, but never the very list the player started from.</summary>
    [Test]
    public void PickOneForMeNeverHandsTheEnemyYourOwnList()
    {
        string tag = Guid.NewGuid().ToString("N")[..6];
        string folder = StrikeTeamFiles.FolderFor(Game)!;
        Directory.CreateDirectory(folder);
        BuiltArmyFile source = ForgeArmy();
        source.Name = $"zz Only {tag}";
        source.Selections = null;   // as a plain folder army would be after a save
        string onlyPath = Remember(Path.Combine(folder, $"zz Only {tag}.fdgarmy"));
        Assert.That(StrikeTeamFiles.Save(onlyPath, source), Is.True);

        var screen = new StrikeTeamScreen();
        screen.Open(Game);
        screen.OnShown();
        screen.StartNew();

        int index = screen.ArmyIndexForTest(onlyPath);
        Assume.That(index, Is.GreaterThanOrEqualTo(0));
        screen.UseExistingArmy(index);
        screen.NewNameForTest = $"zz Mine {tag}";

        // Pick repeatedly: whatever else is in the folder, it must never be the list we started from.
        for (int i = 0; i < 20; i++)
        {
            screen.EnemyNameForTest = "";
            screen.PickEnemyForMe();
            if (screen.EnemyNameForTest.Length == 0) break;   // nothing else to pick
            Assert.That(screen.EnemyNameForTest, Does.Not.StartWith($"zz Only {tag}"),
                "the enemy must not be handed the same army the player started from.");
        }
        Assert.That(File.Exists(onlyPath), Is.True, "starting from a folder army leaves the original untouched.");
        Assert.That(StrikeTeamFiles.Load(onlyPath)!.StrikeTeam, Is.Null);
    }

    [Test]
    public void NamesMustBeGivenAndMustDiffer()
    {
        StrikeTeamScreen screen = ScreenThatBuilds(new Queue<BuiltArmyFile>(new[] { ForgeArmy(), ForgeArmy() }), new List<string>());
        screen.StartNew();
        screen.BuildYourArmy();

        screen.NewNameForTest = "   ";
        Assert.That(screen.YourNameProblem(), Is.EqualTo("Name your army."));
        Assert.That(screen.NewProblem(), Is.EqualTo("Your army: Name your army."), "the wizard's footer says what's missing");

        screen.NewNameForTest = "zz Same Name";
        screen.BuildEnemy();
        screen.EnemyNameForTest = "ZZ same name";
        Assert.That(screen.EnemyNameProblem(), Is.EqualTo("Give the enemy a different name from yours."));
        screen.EnemyNameForTest = "";
        Assert.That(screen.EnemyNameProblem(), Is.EqualTo("Name the enemy army."));

        screen.Finish();
        Assert.That(screen.ViewForTest, Is.EqualTo(StrikeTeamScreen.EView.New), "START CAMPAIGN does nothing while something is missing");
    }

    /// <summary>Backing out of the Forge returns to the wizard where it was, not to the list.</summary>
    [Test]
    public void CancellingTheForgeReturnsToTheWizard()
    {
        var screen = new StrikeTeamScreen();
        screen.Open(Game);
        screen.OnBuildArmy = build => { build.OnCancel(); screen.OnShown(); return true; };
        screen.OnShown();
        screen.StartNew();
        screen.BuildYourArmy();

        Assert.That(screen.ViewForTest, Is.EqualTo(StrikeTeamScreen.EView.New));
        Assert.That(screen.NewTabForTest, Is.EqualTo(StrikeTeamScreen.ENewTab.YourArmy));
    }

    [Test]
    public void AnOrdinaryReturnToTheScreenStillOpensTheList()
    {
        var screen = new StrikeTeamScreen();
        screen.Open(Game);
        screen.OnShown();
        screen.StartNew();
        screen.OnShown();   // e.g. came back from the main menu
        Assert.That(screen.ViewForTest, Is.EqualTo(StrikeTeamScreen.EView.List),
            "only a return from the Forge keeps the wizard; everything else lands on the list as before.");
    }
}
